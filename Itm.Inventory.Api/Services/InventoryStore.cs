using Itm.Inventory.Api.Dtos;

namespace Itm.Inventory.Api.Services;

public class InventoryStore
{
    public List<InventoryDto> Items { get; }

    public InventoryStore()
    {
        Items = new List<InventoryDto>
        {
            new(1, 50, "LAPTOP-DELL"),
            new(2, 0,  "MOUSE-GAMER")
        };
    }
}