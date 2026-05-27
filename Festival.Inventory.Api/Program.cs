using Festival.Inventory.Api.Dtos;
using Festival.Inventory.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using System.Text;


internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- 1. ZONA DE SERVICIOS ---

        // gRPC: Comunicación binaria de alta velocidad con Order.Api
        builder.Services.AddGrpc();
        builder.Services.AddEndpointsApiExplorer();

        // Swagger con botón Authorize (igual semántica que en clase)
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Festival Inventory API",
                Version = "v1",
                Description = "Microservicio de stock de boletas - Festival de los Dos Mundos"
            });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Ingrese el token JWT: Bearer {su_token}"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
            });
        });

        // JWT - Mismo patrón que en clase: leemos de appsettings, no quemamos strings
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
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(secretKey)
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Administrador"));
        });

        builder.Services.AddHttpContextAccessor();

        // Store singleton compartido entre REST y gRPC
        builder.Services.AddSingleton<FestivalInventoryStore>();

        var app = builder.Build();

        // --- 2. ZONA DE MIDDLEWARE ---
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        // --- 3. ZONA DE DATOS (Simulación de BD en memoria) ---
        // FestivalInventoryStore singleton registrado arriba

        // --- 4. ZONA DE ENDPOINTS REST (Para consultas admin y health checks) ---

        // GET /api/inventory/{eventId}/{sede} -> Consultar stock de boletas
        app.MapGet("/api/inventory/{eventId}/{sede}", (int eventId, string sede, HttpContext httpContext, ILogger<Program> logger, FestivalInventoryStore store) =>
        {
            var correlationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? "SIN-ID";

            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                var item = store.Items.FirstOrDefault(b => b.EventId == eventId && b.Sede == sede);
                return item is not null ? Results.Ok(item) : Results.NotFound();
            }
        })
        .RequireAuthorization()
        .WithName("GetInventario");

        // Internal SAGA endpoint - in production, restrict to internal network via Kubernetes NetworkPolicy, not JWT role
        app.MapPost("/api/inventory/reduce", (ReservarBoletaDto request, FestivalInventoryStore store) =>
        {
            var item = store.Items.FirstOrDefault(b => b.EventId == request.EventId && b.Sede == request.Sede);
            if (item is null) return Results.NotFound(new { Error = "Evento no encontrado en bodega" });
            if (item.BoletasDisponibles < request.Quantity)
                return Results.BadRequest(new { Error = "No hay suficientes boletas", Disponibles = item.BoletasDisponibles });

            var index = store.Items.IndexOf(item);
            store.Items[index] = item with
            {
                BoletasDisponibles = item.BoletasDisponibles - request.Quantity,
                BoletasVendidas = item.BoletasVendidas + request.Quantity
            };

            return Results.Ok(new { Message = "Boletas reservadas", BoletasRestantes = store.Items[index].BoletasDisponibles });
        })
        .RequireAuthorization(); // Internal SAGA endpoint - in production, restrict to internal network via Kubernetes NetworkPolicy, not JWT role

        // Internal SAGA endpoint - in production, restrict to internal network via Kubernetes NetworkPolicy, not JWT role
        app.MapPost("/api/inventory/release", (ReservarBoletaDto request, FestivalInventoryStore store) =>
        {
            var item = store.Items.FirstOrDefault(b => b.EventId == request.EventId && b.Sede == request.Sede);
            if (item is null) return Results.NotFound();

            var index = store.Items.IndexOf(item);
            store.Items[index] = item with
            {
                BoletasDisponibles = item.BoletasDisponibles + request.Quantity,
                BoletasVendidas = Math.Max(0, item.BoletasVendidas - request.Quantity)
            };

            Console.WriteLine($"[COMPENSACIÓN] Se devolvieron {request.Quantity} boletas. " +
                              $"Evento={request.EventId} | Sede={request.Sede} | " +
                              $"Nuevo stock: {store.Items[index].BoletasDisponibles}");

            return Results.Ok(new { Message = "Boletas liberadas por compensación SAGA", Stock = store.Items[index].BoletasDisponibles });
        })
        .RequireAuthorization(); // Internal SAGA endpoint - in production, restrict to internal network via Kubernetes NetworkPolicy, not JWT role

        // --- 5. gRPC: El superhéroe de velocidad ---
        app.MapGrpcService<GrpcInventoryService>();

        // Endpoint de salud para Kubernetes Liveness Probe
        app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Inventory.Api" }));

        app.Run();
    }
}