using Elastic.Clients.Elasticsearch;
using Itm.Search.Api.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ElasticsearchClient>(sp =>
{
    var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Elasticsearch");

    var url = config["ElasticsearchUrl"];
    if (string.IsNullOrWhiteSpace(url) || url == "SECRET_REPLACED_BY_ENV")
    {
        logger.LogWarning("ElasticsearchUrl no está configurada o contiene un placeholder; se usará el valor por defecto http://localhost:9200");
        url = "http://localhost:9200";
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        // intentar añadir esquema http si falta
        if (!Uri.TryCreate("http://" + url, UriKind.Absolute, out uri))
        {
            throw new InvalidOperationException($"Valor inválido para ElasticsearchUrl: '{url}'");
        }
    }

    var settings = new ElasticsearchClientSettings(uri)
        .DefaultIndex("itm-events");
    return new ElasticsearchClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    return new QdrantClient("localhost", 6333);
});

var app = builder.Build();

app.UseMiddleware<Itm.Search.Api.Shared.Middleware.CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var esClient = app.Services.GetRequiredService<ElasticsearchClient>();

var sampleEvents = new List<SearchEvent>
{
    new() { EventId = 1, Name = "Festival de los Dos Mundos", Description = "Festival internacional de música y arte en Spoleto", City = "Spoleto", Genre = "Música" },
    new() { EventId = 2, Name = "Jazz en la Montaña", Description = "Concierto de jazz al aire libre con vistas panorámicas", City = "Medellín", Genre = "Jazz" },
    new() { EventId = 3, Name = "Feria del Libro ITM", Description = "Feria literaria con autores nacionales e internacionales", City = "Medellín", Genre = "Literatura" },
    new() { EventId = 4, Name = "Rock al Parque", Description = "Festival de rock con bandas emergentes y consagradas", City = "Bogotá", Genre = "Rock" },
    new() { EventId = 5, Name = "Exposición de Arte Digital", Description = "Muestra de arte digital y realidad virtual", City = "Bogotá", Genre = "Arte" }
};

try
{
    var existsResponse = await esClient.Indices.ExistsAsync("itm-events");
    if (!existsResponse.Exists)
    {
        await esClient.Indices.CreateAsync("itm-events");
        foreach (var evt in sampleEvents)
        {
            await esClient.IndexAsync(evt, idx => idx.Index("itm-events").Id(evt.EventId));
        }
    }
}
catch { }

app.MapGet("/api/search/events", async (string? q, ElasticsearchClient es) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.Ok(sampleEvents);

    var response = await es.SearchAsync<SearchEvent>(s => s
        .Index("itm-events")
        .Query(qry => qry
            .Match(m => m
                .Field(f => f.Name)
                .Query(q)
            )
        )
    );

    var results = response.Documents.ToList();
    if (results.Count == 0)
    {
        response = await es.SearchAsync<SearchEvent>(s => s
            .Index("itm-events")
            .Query(qry => qry
                .Match(m => m
                    .Field(f => f.Description)
                    .Query(q)
                )
            )
        );
        results = response.Documents.ToList();
    }

    return Results.Ok(results);
});

app.MapGet("/api/search/events/semantic", async (string? vibe, QdrantClient qdrant) =>
{
    var eventVectors = new Dictionary<int, float[]>
    {
        { 1, new[] { 0.9f, 0.1f, 0.8f, 0.2f, 0.5f } },
        { 2, new[] { 0.2f, 0.9f, 0.3f, 0.7f, 0.1f } },
        { 3, new[] { 0.1f, 0.2f, 0.1f, 0.3f, 0.9f } },
        { 4, new[] { 0.3f, 0.8f, 0.2f, 0.9f, 0.1f } },
        { 5, new[] { 0.7f, 0.1f, 0.9f, 0.1f, 0.3f } }
    };

    try
    {
        await qdrant.CreateCollectionAsync("itm-events-vectors", new VectorParams { Size = 5, Distance = Distance.Cosine });
    }
    catch { }

    var points = eventVectors.Select(kv =>
    {
        var point = new PointStruct
        {
            Id = (ulong)kv.Key,
            Vectors = kv.Value
        };
        point.Payload["event_id"] = kv.Key;
        return point;
    }).ToList();

    await qdrant.UpsertAsync("itm-events-vectors", points);

    float[] queryVector;
    if (string.IsNullOrWhiteSpace(vibe))
    {
        queryVector = new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
    }
    else
    {
        var lower = vibe.ToLowerInvariant();
        queryVector = lower switch
        {
            var v when v.Contains("musica") || v.Contains("concierto") || v.Contains("fiesta") => new[] { 0.8f, 0.3f, 0.7f, 0.4f, 0.2f },
            var v when v.Contains("arte") || v.Contains("cultura") || v.Contains("exposicion") => new[] { 0.6f, 0.2f, 0.9f, 0.1f, 0.4f },
            var v when v.Contains("libro") || v.Contains("leer") || v.Contains("literatura") => new[] { 0.1f, 0.2f, 0.1f, 0.3f, 0.9f },
            var v when v.Contains("rock") || v.Contains("banda") || v.Contains("metal") => new[] { 0.3f, 0.9f, 0.2f, 0.9f, 0.1f },
            var v when v.Contains("jazz") || v.Contains("blues") => new[] { 0.2f, 0.9f, 0.3f, 0.7f, 0.1f },
            _ => new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f }
        };
    }

    var searchResult = await qdrant.SearchAsync("itm-events-vectors", queryVector, limit: 3);

    var results = searchResult.Select(r =>
    {
        var eventId = (int)r.Id.Num;
        var evt = sampleEvents.FirstOrDefault(e => e.EventId == eventId);
        return new { Event = evt, Score = r.Score };
    }).Where(r => r.Event is not null).ToList();

    return Results.Ok(results);
});

app.Run();
