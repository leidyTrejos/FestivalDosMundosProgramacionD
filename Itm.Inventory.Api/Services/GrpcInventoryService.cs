using Grpc.Core;
using Itm.Inventory.Api.Protos;

namespace Itm.Inventory.Api.Services;

public class GrpcInventoryService : InventoryService.InventoryServiceBase
{
    private readonly InventoryStore _store;

    public GrpcInventoryService(InventoryStore store)
    {
        _store = store;
    }

    public override Task<StockResponse> CheckStock(StockRequest request, ServerCallContext context)
    {
        var item = _store.Items.FirstOrDefault(p => p.ProductId == request.ProductId);
        var stock = item?.Stock ?? 0;

        return Task.FromResult(new StockResponse
        {
            ProductId = request.ProductId,
            Stock = stock,
            IsAvailable = stock > 0
        });
    }
}
