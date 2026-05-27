namespace Festival.Shared.Events;

public record OrderCreatedEvent(Guid OrderId, int EventId, string Sede, string CustomerEmail, decimal TotalAmount);
