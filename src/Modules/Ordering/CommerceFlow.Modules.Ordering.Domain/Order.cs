namespace CommerceFlow.Modules.Ordering.Domain;

// First real domain slice: just enough to represent "a valid order" and protect its own
// internal consistency. CustomerId, status, and anything persistence-related (identity column,
// CreatedDate, ERP export fields) are deliberately NOT modeled yet -- none of them are needed to
// enforce the two invariants this slice is about, and guessing at them now would be designing
// for a later Application-layer command that doesn't exist yet.
//
// Rules below are extracted from proven M0 evidence, not invented:
// - "at least one item" and "quantity > 0 per line": usp_CreateOrder THROW 51003/51004
//   (docs/acceptance-criteria.md AC9/AC10), messages kept identical on purpose.
// - duplicate ProductId lines are kept separate, not merged: usp_CreateOrder lines 79-82
//   (AC8), characterization-test-proven.
// - TotalAmount is decimal, computed from items: Orders.TotalAmount is decimal, and
//   usp_CreateOrder sets it to SUM(LineTotal) after insert (same proc, lines 84-86).
// Collection encapsulation is NOT something legacy has (Legacy.Models.Order.Items is a fully
// mutable public List<T> -- exactly the anemic-model debt this migration exists to fix); it's
// applied here because it was explicitly asked for, not because legacy evidences it.
public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    private Order(Guid id)
    {
        Id = id;
    }

    // Orders are always created complete -- legacy has no "add a line to an existing order" use
    // case (usp_CreateOrder takes the whole item list in one call), so there is no public
    // AddItem; it would be a capability nothing in evidence supports.
    public static Order Create(IReadOnlyList<(int ProductId, int Quantity, decimal UnitPrice)> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one item.", nameof(lines));
        }

        var order = new Order(Guid.NewGuid());

        foreach (var (productId, quantity, unitPrice) in lines)
        {
            if (quantity <= 0)
            {
                throw new ArgumentException(
                    $"Quantity must be greater than zero for product {productId}.", nameof(lines));
            }

            order._items.Add(new OrderItem(productId, quantity, unitPrice));
        }

        return order;
    }
}
