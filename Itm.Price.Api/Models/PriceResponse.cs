namespace Itm.Price.Api.Models;

public record PriceResponse(int ProductId, decimal Amount, string Currency, string Source);