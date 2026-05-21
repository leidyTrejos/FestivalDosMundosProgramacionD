using Itm.Price.Api.Models;

namespace Itm.Price.Api.Services;

public class PriceDatabase
{
    private readonly List<PriceDto> _prices = new()
    {
        new(1, 2500.00m, "USD", new DateTime(2026, 1, 15)),
        new(2, 45.00m, "USD", new DateTime(2026, 3, 10)),
        new(3, 1200.00m, "USD", new DateTime(2026, 2, 20)),
        new(4, 89.99m, "USD", new DateTime(2026, 4, 1)),
        new(5, 650.00m, "USD", new DateTime(2026, 5, 5))
    };

    public PriceDto? GetPriceById(int productId)
    {
        return _prices.FirstOrDefault(p => p.ProductId == productId);
    }
}