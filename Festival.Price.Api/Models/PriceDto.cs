namespace Festival.Price.Api.Models;

public record PriceDto(int EventId, decimal Amount, string Currency, DateTimeOffset CachedAt);
