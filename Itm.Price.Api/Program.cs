using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using Itm.Price.Api.Models;
using Itm.Price.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSettings["Audience"],
            ValidateLifetime = true,
            RequireExpirationTime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretKey)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<PriceDatabase>();
builder.Services.AddSingleton<CacheMetrics>();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["RedisConnection"];
    options.InstanceName = "ItmPriceCache:";
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

var metrics = app.Services.GetRequiredService<CacheMetrics>();

app.MapGet("/api/prices/{eventId}", async (int eventId, IDistributedCache cache, PriceDatabase db, HttpContext httpContext) =>
{
    var cacheKey = $"price:{eventId}";

    var cachedPrice = await cache.GetStringAsync(cacheKey);

    if (cachedPrice is not null)
    {
        metrics.RecordHit();
        var price = JsonSerializer.Deserialize<PriceResponse>(cachedPrice);
        httpContext.Response.Headers["X-Cache-Hit"] = "true";
        return Results.Ok(price with { Source = "Redis Cache" });
    }

    metrics.RecordMiss();
    var dbPrice = db.GetPriceById(eventId);

    if (dbPrice is null)
        return Results.NotFound(new { Error = "Evento no encontrado" });

    var currentTime = DateTimeOffset.UtcNow;
    var response = new PriceResponse(dbPrice.EventId, dbPrice.Amount, dbPrice.Currency, currentTime, "Database");

    await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
    });

    httpContext.Response.Headers["X-Cache-Hit"] = "false";
    return Results.Ok(response);
})
.RequireAuthorization();

app.MapGet("/api/price/metrics", () =>
{
    return Results.Ok(new
    {
        metrics.CacheHits,
        metrics.CacheMisses,
        metrics.TotalRequests,
        metrics.HitRate
    });
});

app.Run();
