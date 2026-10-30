namespace CommerceFlow.Modules.Inventory.Domain;

// First real domain slice for Inventory, mirroring exactly how Ordering.Domain started: one
// aggregate, invariants extracted from proven evidence, nothing speculative. Price/SKU/Name stay
// a Product/catalog concern this aggregate does not model -- Stock's only job is "can this many
// units be reserved right now, and if so, atomically take them."
//
// Rules below are extracted from proven M0 evidence, not invented:
// - "cannot reserve more than what's on hand": usp_CreateOrder's conditional UPDATE
//   (`UPDATE dbo.Products SET StockQuantity -= @Qty WHERE StockQuantity >= @Qty`, checked via
//   `@@ROWCOUNT = 0` -> THROW 51002 "Insufficient stock for product X.").
// - StockQuantity can never go negative: CK_Products_StockQuantity CHECK (StockQuantity >= 0).
// Canonical ascending-ProductId lock ordering (deadlock avoidance across multiple products
// reserved in one business transaction) is NOT this aggregate's invariant -- it only matters when
// coordinating several Stock instances at once, which is an Application-level concern for a
// later slice, not something a single aggregate can own.
//
// Current status, recorded explicitly (2026-10-30), not to be read as "M3 done":
// - This is a verified domain model, not yet wired into the production create-order flow.
//   CreateOrderUseCase/OrderWriter never construct or call a Stock instance.
// - The real owner of stock correctness today is still usp_CreateOrder's conditional UPDATE and
//   its surrounding transaction -- that is what actually protects production data right now,
//   not this class.
// - Canonical ProductId-ascending ordering only reduces one specific deadlock pattern (two
//   orders touching the same products in different submission order); it is not a blanket
//   guarantee against every possible deadlock.
// - How a future C# extraction would own the transaction across Ordering and Inventory, and
//   coordinate multiple Stock instances atomically, is an open design question -- not decided,
//   not implied by this class existing.
public sealed class Stock
{
    public int ProductId { get; }

    public int QuantityOnHand { get; private set; }

    private Stock(int productId, int quantityOnHand)
    {
        ProductId = productId;
        QuantityOnHand = quantityOnHand;
    }

    public static Stock Create(int productId, int quantityOnHand)
    {
        if (quantityOnHand < 0)
        {
            throw new ArgumentException("Quantity on hand cannot be negative.", nameof(quantityOnHand));
        }

        return new Stock(productId, quantityOnHand);
    }

    // Returning false for insufficient stock, not throwing: unlike an invalid quantity (a caller
    // precondition violation), "not enough stock right now" is an expected business outcome that
    // depends on this aggregate's own mutable state -- the same reasoning that kept
    // CreateOrderResult a value, not an exception, at the Ordering write path's boundary.
    public bool TryReserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentException("Reservation quantity must be greater than zero.", nameof(quantity));
        }

        if (quantity > QuantityOnHand)
        {
            return false;
        }

        QuantityOnHand -= quantity;
        return true;
    }
}
