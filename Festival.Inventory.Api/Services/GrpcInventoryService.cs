using Festival.Inventory.Api.Protos;
using Grpc.Core;

namespace Festival.Inventory.Api.Services;

public class GrpcInventoryService : InventoryService.InventoryServiceBase
{
    private readonly FestivalInventoryStore _store;

    public GrpcInventoryService(FestivalInventoryStore store)
    {
        _store = store;
    }

    public override Task<StockResponse> CheckStock(StockRequest request, ServerCallContext context)
    {
        var totalStock = _store.Items
            .Where(b => b.EventId == request.ProductId)
            .Sum(b => b.BoletasDisponibles);

        return Task.FromResult(new StockResponse
        {
            ProductId = request.ProductId,
            Stock = totalStock,
            IsAvailable = totalStock > 0
        });
    }
}
