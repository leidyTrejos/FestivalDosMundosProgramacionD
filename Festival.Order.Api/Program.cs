using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Festival.Inventory.Api.Protos;
using Festival.Shared.Events;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// JWT authentication
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

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// gRPC client for Festival.Inventory.Api (Kestrel endpoint: port 5101)
builder.Services.AddGrpcClient<InventoryService.InventoryServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5101");
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient("InventoryClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5100");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient("PriceClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:5300");
    client.Timeout = TimeSpan.FromSeconds(5);
});

// MassTransit/RabbitMQ producer
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq://localhost", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// SAGA endpoint: create order
app.MapPost("/api/orders", async (CreateOrderDto order, IHttpClientFactory factory, IBus bus, InventoryService.InventoryServiceClient grpcClient, HttpContext httpContext) =>
{
    var customerEmail = httpContext.User.FindFirstValue(ClaimTypes.Email) ?? "anonimo@festival.com";

    // PASO 0: Verificar stock via gRPC
    var stockCheck = await grpcClient.CheckStockAsync(new StockRequest { ProductId = order.ProductId });
    if (!stockCheck.IsAvailable)
    {
        return Results.BadRequest($"Stock insuficiente. Solo quedan {stockCheck.Stock} unidades. Transaccion abortada.");
    }

    var invClient = factory.CreateClient("InventoryClient");

    // PASO 1: Intentar reservar Stock
    var reduceResponse = await invClient.PostAsJsonAsync("/api/inventory/reduce", order);
    if (!reduceResponse.IsSuccessStatusCode)
    {
        return Results.BadRequest("No se pudo reservar el stock. Transaccion abortada.");
    }

    try
    {
        // PASO 2: Obtener precio
        var priceClient = factory.CreateClient("PriceClient");
        var priceResponse = await priceClient.GetFromJsonAsync<PriceResponse>($"/api/prices/{order.ProductId}");
        if (priceResponse is null)
        {
            throw new InvalidOperationException("No se pudo obtener el precio del producto.");
        }

        // PASO 3: Pago (simulacion ~50% exito)
        var random = new Random();
        var paymentSuccess = random.Next(0, 10) > 5;
        if (!paymentSuccess)
        {
            throw new InvalidOperationException("Fondos insuficientes en la tarjeta.");
        }

        // PASO 4: Publicar evento OrderCreated
        var orderId = Guid.NewGuid();
        var orderEvent = new OrderCreatedEvent(orderId, order.ProductId, "Medellin", customerEmail, priceResponse.Amount * order.Quantity);
        await bus.Publish(orderEvent);

        Console.WriteLine($"[SAGA] Orden {orderId} creada. Evento OrderCreated publicado.");

        return Results.Ok(new
        {
            OrderId = orderId,
            Message = "Orden creada y pagada exitosamente.",
            ProductPrice = priceResponse.Amount,
            Currency = priceResponse.Currency,
            PriceSource = priceResponse.Source
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Fallo el pago: {ex.Message}. Iniciando compensacion...");

        var compensateResponse = await invClient.PostAsJsonAsync("/api/inventory/release", order);
        if (compensateResponse.IsSuccessStatusCode)
        {
            return Results.Problem("El pago fallo. El stock fue devuelto correctamente. Intente de nuevo.");
        }

        Console.WriteLine("[CRITICAL] Fallo la compensacion. Datos inconsistentes, requiere intervencion manual.");
        return Results.Problem("Error critico del sistema. Contacte soporte.");
    }
});

app.Run();

public record CreateOrderDto(int ProductId, int Quantity);
public record PriceResponse(int EventId, decimal Amount, string Currency, DateTimeOffset CachedAt, string Source);
