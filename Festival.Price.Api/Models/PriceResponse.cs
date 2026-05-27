namespace Festival.Price.Api.Models;

public record PriceResponse(int EventId, decimal Amount, string Currency, DateTimeOffset CachedAt, string Source);
