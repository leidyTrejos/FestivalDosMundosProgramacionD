using Itm.Notification.Api.Consumers;
using Itm.Notification.Api.Dtos;
using Itm.Notification.Api.Hubs;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("Internal", client =>
{
    client.BaseAddress = new Uri("http://localhost:5400");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMqConnection"] ?? "amqps://wqwoltap:bYzWB8MvzX891TQSA8YY5HB8ePSdLWda@turkey.rmq.cloudamqp.com/wqwoltap");
        cfg.ReceiveEndpoint("notification-order-created-queue", e =>
        {
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

var app = builder.Build();

app.UseCors();

app.MapPost("/api/notifications/ticket-ready", async (TicketReadyDto dto, IHubContext<TicketHub> hubContext) =>
{
    await hubContext.Clients.Group(dto.CustomerEmail).SendAsync("ReceiveTicket", dto);
    return Results.Ok(new { Message = "Notificación enviada." });
});

app.MapHub<TicketHub>("/hubs/tickets");

app.Run();
