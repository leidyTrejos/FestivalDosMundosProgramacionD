using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// =========================
// SEGURIDAD PERIMETRAL: Validación JWT en el Gateway
// =========================
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

// =========================
// Configuración de YARP
// =========================
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// =========================
// CONTROL DE MULTITUDES: Rate Limiting con política por ruta
// =========================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("per-route", httpContext =>
    {
        var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "";

        int permitLimit;
        TimeSpan window;

        if (path.Contains("/api/orders"))
        {
            permitLimit = 5;
            window = TimeSpan.FromSeconds(10);
        }
        else if (path.Contains("/bodega") || path.Contains("/api/products") || path.Contains("/api/price"))
        {
            permitLimit = 30;
            window = TimeSpan.FromSeconds(10);
        }
        else
        {
            permitLimit = 10;
            window = TimeSpan.FromSeconds(10);
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString(),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// =========================
// Middleware pipeline
// =========================
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// =========================
// Rutas del Gateway con políticas específicas
// =========================
app.MapReverseProxy()
    .RequireAuthorization()
    .RequireRateLimiting("per-route");

app.Run();