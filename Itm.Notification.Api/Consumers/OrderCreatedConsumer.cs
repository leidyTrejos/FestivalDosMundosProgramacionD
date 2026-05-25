using System.Net.Http.Json;
using Itm.Notification.Api.Dtos;
using Itm.Shared.Events;
using MassTransit;

namespace Itm.Notification.Api.Consumers;

public class OrderCreatedConsumer : IConsumer<OrderCreatedEvent>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrderCreatedConsumer(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task Consume(ConsumeContext<OrderCreatedEvent> context)
    {
        var notification = new TicketReadyDto(
            context.Message.OrderId,
            context.Message.CustomerEmail,
            $"QR-{context.Message.OrderId}-demo",
            DateTimeOffset.UtcNow);

        var client = _httpClientFactory.CreateClient("Internal");
        await client.PostAsJsonAsync("/api/notifications/ticket-ready", notification);
    }
}
