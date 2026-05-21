namespace Itm.Price.Api.Models;

public record PriceDto(int ProductId, decimal Amount, string Currency, DateTime LastUpdated);