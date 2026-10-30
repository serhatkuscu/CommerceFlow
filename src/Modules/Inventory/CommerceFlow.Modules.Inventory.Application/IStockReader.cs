using CommerceFlow.Modules.Inventory.Domain;

namespace CommerceFlow.Modules.Inventory.Application;

public interface IStockReader
{
    Task<Stock?> GetByProductIdAsync(int productId, CancellationToken ct);
}
