using Itm.Price.Api.Models;

namespace Itm.Price.Api.Services;

public class PriceDatabase
{
    private readonly List<PriceDto> _prices = new()
    {
        new(1, 150.00m, "USD", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
        new(2, 85.50m, "USD", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
        new(3, 220.00m, "USD", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero))
    };

    public PriceDto? GetPriceById(int eventId)
    {
        return _prices.FirstOrDefault(p => p.EventId == eventId);
    }
}
