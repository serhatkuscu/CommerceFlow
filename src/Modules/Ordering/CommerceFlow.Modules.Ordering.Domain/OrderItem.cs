namespace CommerceFlow.Modules.Ordering.Domain;

// Constructor is internal: an OrderItem only ever comes into existence as part of an Order,
// never standalone -- this is what AggregateRoot ownership actually means in code, not just
// in a diagram.
public sealed class OrderItem
{
    public int ProductId { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }

    // Computed, not stored: legacy persists LineTotal as its own column (a real drift risk --
    // see docs/legacy-technical-debt.md), the domain model doesn't repeat that mistake. The
    // *value* still matches legacy exactly: Quantity * UnitPrice (usp_CreateOrder,
    // 0006_CreateStoredProcedure_CreateOrder.sql line 81).
    public decimal LineTotal => Quantity * UnitPrice;

    internal OrderItem(int productId, int quantity, decimal unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
