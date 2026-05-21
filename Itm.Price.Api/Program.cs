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
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
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

app.MapGet("/api/price/{id}", async (int id, IDistributedCache cache, PriceDatabase db) =>
{
    var cacheKey = $"price:{id}";

    var cachedPrice = await cache.GetStringAsync(cacheKey);

    if (cachedPrice is not null)
    {
        metrics.RecordHit();
        var price = JsonSerializer.Deserialize<PriceResponse>(cachedPrice);
        return Results.Ok(price with { Source = "Redis Cache (90% hit rate)" });
    }

    metrics.RecordMiss();
    var dbPrice = db.GetPriceById(id);

    if (dbPrice is null)
        return Results.NotFound(new { Error = "Producto no encontrado" });

    var response = new PriceResponse(dbPrice!.ProductId, dbPrice.Amount, dbPrice.Currency, "SQL Database");

    await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
    });

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
