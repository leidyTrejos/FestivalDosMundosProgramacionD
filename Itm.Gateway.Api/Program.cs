using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Durante desarrollo, asegurarnos de que Kestrel escuche en todas las interfaces
// para que el emulador Android (10.0.2.2) pueda conectarse al host de desarrollo.
// Nota: esto es seguro en entorno de desarrollo, no exponga 0.0.0.0 en producción sin las medidas adecuadas.
builder.WebHost.UseUrls("http://0.0.0.0:5183");

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
// Endpoint de emisión de JWT (NO pasa por autenticación ni rate limiting estricto)
// =========================
app.MapPost("/auth/token", (LoginRequest request) =>
{
    // Hardcodeado para demo — dos usuarios de prueba
    var user = (request.Email, request.Password) switch
    {
        ("admin@itm.edu.co", "admin123") => (Email: "admin@itm.edu.co", Role: "Administrador"),
        ("cliente@itm.edu.co", "cliente123") => (Email: "cliente@itm.edu.co", Role: "Cliente"),
        _ => (Email: (string?)null, Role: (string?)null)
    };

    if (user.Email is null)
        return Results.Unauthorized();

    var jwtSettingsSection = builder.Configuration.GetSection("JwtSettings");
    var secretKey = Encoding.UTF8.GetBytes(jwtSettingsSection["SecretKey"]!);
    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Issuer = jwtSettingsSection["Issuer"],
        Audience = jwtSettingsSection["Audience"],
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        }),
        Expires = DateTime.UtcNow.AddHours(8),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(secretKey), SecurityAlgorithms.HmacSha256)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    var tokenString = tokenHandler.WriteToken(token);

    return Results.Ok(new { token = tokenString, email = user.Email, role = user.Role });
});

// =========================
// Rutas del Gateway con políticas específicas
// =========================
app.MapReverseProxy()
    .RequireAuthorization()
    .RequireRateLimiting("per-route");

app.Run();

// DTO local para login
public record LoginRequest(string Email, string Password);