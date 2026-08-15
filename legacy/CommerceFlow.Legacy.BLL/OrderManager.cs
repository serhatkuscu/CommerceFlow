using CommerceFlow.Legacy.DAL;
using CommerceFlow.Legacy.Models;

namespace CommerceFlow.Legacy.BLL;

// Deliberately thin: usp_CreateOrder already owns the real invariants (stock, all-or-nothing,
// quantity, customer/product existence) -- a common real-world drift where "business logic"
// ends up living in the stored procedure rather than the BLL nominally responsible for it.
// This class exists as its own layer (matching the app's 3-tier shape) and owns only what
// genuinely belongs above the database: guards that would otherwise crash before ever reaching
// the DAL, and orchestration. No interface, no DI container -- news up its DAL directly.
public class OrderManager
{
    private readonly OrderDataAccess _dataAccess;

    public OrderManager()
    {
        _dataAccess = new OrderDataAccess();
    }

    public int CreateOrder(int customerId, IReadOnlyList<OrderLineRequest>? items)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        return _dataAccess.CreateOrder(customerId, items);
    }

    public Order? GetOrderById(int orderId)
    {
        return _dataAccess.GetOrderById(orderId);
    }
}
