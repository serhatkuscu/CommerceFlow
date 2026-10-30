namespace CommerceFlow.Modules.Ordering.Application;

// Deliberately CustomerId + {ProductId, Quantity} only -- there is no UnitPrice slot anywhere on
// this type, so a client cannot supply or influence a line's price even by trying to. Pricing
// stays resolved server-side inside usp_CreateOrder, exactly as it already is for the legacy
// write path.
public sealed record CreateOrderRequest(int CustomerId, IReadOnlyList<CreateOrderLine>? Items);

public sealed record CreateOrderLine(int ProductId, int Quantity);
