using Festival.Inventory.Api.Dtos;

namespace Festival.Inventory.Api.Services;

public class FestivalInventoryStore
{
    public List<BoletaInventarioDto> Items { get; }

    public FestivalInventoryStore()
    {
        Items = new List<BoletaInventarioDto>
        {
            new(1, "Medellin", 25000, 0),
            new(1, "Madrid",   25000, 0),
            new(2, "Medellin",  5000, 0),
            new(2, "Madrid",    5000, 0),
        };
    }
}
