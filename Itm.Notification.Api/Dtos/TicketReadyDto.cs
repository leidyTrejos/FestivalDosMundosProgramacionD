namespace Itm.Notification.Api.Dtos;

public record TicketReadyDto(Guid OrderId, string CustomerEmail, string QrCodeBase64, DateTimeOffset IssuedAt);
