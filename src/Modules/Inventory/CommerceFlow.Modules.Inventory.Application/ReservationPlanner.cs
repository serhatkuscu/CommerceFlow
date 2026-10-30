using CommerceFlow.Modules.Inventory.Domain;

namespace CommerceFlow.Modules.Inventory.Application;

public sealed record ReservationLine(int ProductId, int Quantity);

public sealed record ReservationPlanResult(bool Succeeded, int? InsufficientProductId);

// Pure, in-memory decision over a snapshot of Stock instances -- no database, no transaction,
// not called from the production create-order flow. Its only job right now is to let a
// characterization test prove this C# decision agrees with usp_CreateOrder's real SQL decision
// for the same inputs. See Stock.cs for why that "not yet wired in" status matters.
//
// Multi-product reservation is deliberately not a single Stock aggregate's job: a single Stock
// can only say yes/no for itself. Deciding "all these products, or none of them" needs an outside
// coordinator -- this class -- and that coordinator does NOT undo an already-succeeded
// Stock.TryReserve on a later line's failure (see TryPlan below). That is intentional for this
// slice (nothing here is ever persisted), but it is exactly the gap a future real integration
// would have to close before this logic could safely own production data.
public static class ReservationPlanner
{
    // Mirrors usp_CreateOrder exactly: duplicate ProductId lines are summed (GROUP BY ProductId),
    // and products are processed in ascending ProductId order (the stored procedure's canonical
    // lock order), not request order.
    public static ReservationPlanResult TryPlan(
        IReadOnlyDictionary<int, Stock> stockByProductId, IReadOnlyList<ReservationLine> lines)
    {
        var groupedAscending = lines
            .GroupBy(line => line.ProductId)
            .Select(group => new ReservationLine(group.Key, group.Sum(line => line.Quantity)))
            .OrderBy(line => line.ProductId);

        foreach (var line in groupedAscending)
        {
            if (!stockByProductId[line.ProductId].TryReserve(line.Quantity))
            {
                return new ReservationPlanResult(Succeeded: false, InsufficientProductId: line.ProductId);
            }
        }

        return new ReservationPlanResult(Succeeded: true, InsufficientProductId: null);
    }
}
