using System.Text.Json;
using Itm.Notification.Api.Dtos;
using Itm.Notification.Api.Hubs;
using Itm.Shared.Events;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using QRCoder;

namespace Itm.Notification.Api.Consumers;

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IHubContext<TicketHub> _hubContext;

    public OrderCreatedConsumer(IHubContext<TicketHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var orderId = context.Message.OrderId;
        var email = context.Message.CustomerEmail;

        // Generar QR real en base64 usando QRCoder
        var qrContent = JsonSerializer.Serialize(new
        {
            orderId,
            context.Message.ProductId,
            context.Message.TotalAmount,
            issuedAt = DateTimeOffset.UtcNow
        });

        string qrBase64;
        using (var qrGenerator = new QRCodeGenerator())
        {
            var qrData = qrGenerator.CreateQrCode(qrContent, QRCodeGenerator.ECCLevel.Q);
            using (var qrCode = new PngByteQRCode(qrData))
            {
                var qrBytes = qrCode.GetGraphic(20);
                qrBase64 = Convert.ToBase64String(qrBytes);
            }
        }

        var notification = new TicketReadyDto(
            orderId,
            email,
            qrBase64,
            DateTimeOffset.UtcNow);

        // Enviar directamente via SignalR sin HTTP round-trip
        await _hubContext.Clients.Group(email).SendAsync("ReceiveTicket", JsonSerializer.Serialize(notification));

        Console.WriteLine($"[NOTIFICATION] Ticket {orderId} enviado por SignalR a {email}");
    }
}
