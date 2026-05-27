using Elastic.Clients.Elasticsearch;
using Festival.Search.Api.Models;
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
        logger.LogWarning("ElasticsearchUrl no configurada; usando http://localhost:9200");
        url = "http://localhost:9200";
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        if (!Uri.TryCreate("http://" + url, UriKind.Absolute, out uri))
            throw new InvalidOperationException($"ElasticsearchUrl invalido: '{url}'");
    }

    var settings = new ElasticsearchClientSettings(uri)
        .DefaultIndex("festival-events");
    return new ElasticsearchClient(settings);
});

builder.Services.AddSingleton(sp =>
{
    return new QdrantClient("localhost", 6333);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var esClient = app.Services.GetRequiredService<ElasticsearchClient>();

// Eventos de muestra del Festival de los Dos Mundos (sedes Medellin y Madrid)
var sampleEvents = new List<SearchEvent>
{
    new() { EventId = 1, Name = "Festival de los Dos Mundos - Medellin", Description = "Concierto inaugural con artistas internacionales en el Teatro Metropolitano", City = "Medellin", Genre = "Musica" },
    new() { EventId = 2, Name = "Festival de los Dos Mundos - Madrid", Description = "Gran cierre del festival en el Teatro Real con orquesta sinfonica", City = "Madrid", Genre = "Musica" },
    new() { EventId = 3, Name = "Exposicion de Arte Digital Dos Mundos", Description = "Artistas de Medellin y Madrid exponen obras de arte digital y VR", City = "Medellin", Genre = "Arte" },
    new() { EventId = 4, Name = "Jazz en la Plaza de Espana", Description = "Noche de jazz con musicos de Colombia y Espana en Madrid", City = "Madrid", Genre = "Jazz" },
    new() { EventId = 5, Name = "Feria Gastronomica Dos Mundos", Description = "Sabores tipicos de Colombia y Espana en un solo lugar", City = "Medellin", Genre = "Gastronomia" }
};

try
{
    var existsResponse = await esClient.Indices.ExistsAsync("festival-events");
    if (!existsResponse.Exists)
    {
        await esClient.Indices.CreateAsync("festival-events");
        foreach (var evt in sampleEvents)
        {
            await esClient.IndexAsync(evt, idx => idx.Index("festival-events").Id(evt.EventId));
        }
    }
}
catch { }

app.MapGet("/api/search/events", async (string? q, ElasticsearchClient es) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.Ok(sampleEvents);

    var response = await es.SearchAsync<SearchEvent>(s => s
        .Index("festival-events")
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
            .Index("festival-events")
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
        await qdrant.CreateCollectionAsync("festival-events-vectors", new VectorParams { Size = 5, Distance = Distance.Cosine });
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

    await qdrant.UpsertAsync("festival-events-vectors", points);

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
            var v when v.Contains("jazz") || v.Contains("blues") => new[] { 0.2f, 0.9f, 0.3f, 0.7f, 0.1f },
            _ => new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f }
        };
    }

    var searchResult = await qdrant.SearchAsync("festival-events-vectors", queryVector, limit: 3);

    var results = searchResult.Select(r =>
    {
        var eventId = (int)r.Id.Num;
        var evt = sampleEvents.FirstOrDefault(e => e.EventId == eventId);
        return new { Event = evt, Score = r.Score };
    }).Where(r => r.Event is not null).ToList();

    return Results.Ok(results);
});

app.Run();
