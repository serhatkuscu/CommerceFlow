namespace CommerceFlow.Modules.Ordering.Application;

// Deliberately does not construct the write-side Order aggregate (Order.Create/OrderItem).
// Order's invariants assume a caller-supplied UnitPrice, but this flow never accepts a price
// from the client -- pricing, stock, and customer validation all still live in usp_CreateOrder,
// not here. Routing through Order would mean either inventing a fake price just to satisfy its
// constructor, or re-implementing the stored procedure's checks in C# -- both wrong, and neither
// is what this slice is for. This is a documented, open transitional state: Order has no role on
// the production write path yet. It gets one once pricing/customer/stock modeling actually moves
// into this module -- not before.
public sealed class CreateOrderUseCase(IOrderWriter writer)
{
    public Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken ct) =>
        writer.CreateAsync(request, ct);
}
