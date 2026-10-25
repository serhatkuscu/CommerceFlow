using CommerceFlow.Modules.Ordering.Domain;

namespace CommerceFlow.Modules.Ordering.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void Create_WithValidLines_Succeeds()
    {
        var order = Order.Create(new (int ProductId, int Quantity, decimal UnitPrice)[]
        {
            (1, 2, 249.90m),
            (3, 1, 349.50m)
        });

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(849.30m, order.TotalAmount);
        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void Create_WithNoLines_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Order.Create(Array.Empty<(int, int, decimal)>()));

        Assert.Equal("Order must contain at least one item. (Parameter 'lines')", ex.Message);
    }

    [Fact]
    public void Create_WithNullLines_Throws()
    {
        Assert.Throws<ArgumentException>(() => Order.Create(null!));
    }

    [Fact]
    public void Create_WithZeroQuantity_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Order.Create(new (int, int, decimal)[] { (1, 0, 10m) }));

        Assert.Equal(
            "Quantity must be greater than zero for product 1. (Parameter 'lines')", ex.Message);
    }

    [Fact]
    public void Create_WithNegativeQuantity_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Order.Create(new (int, int, decimal)[] { (1, -1, 10m) }));

        Assert.Equal(
            "Quantity must be greater than zero for product 1. (Parameter 'lines')", ex.Message);
    }

    // AC8: proven legacy behavior -- duplicate ProductId lines are not merged.
    [Fact]
    public void Create_WithDuplicateProductLines_KeepsSeparateItems()
    {
        var order = Order.Create(new (int, int, decimal)[]
        {
            (2, 1, 899.00m),
            (2, 2, 899.00m)
        });

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(2697.00m, order.TotalAmount); // 1*899 + 2*899, not summed into one line first
    }

    [Fact]
    public void Create_MutatingSourceListAfterwards_DoesNotAffectTheOrder()
    {
        var sourceLines = new List<(int ProductId, int Quantity, decimal UnitPrice)> { (1, 2, 249.90m) };

        var order = Order.Create(sourceLines);
        var itemCountBefore = order.Items.Count;
        var totalBefore = order.TotalAmount;

        sourceLines.Add((2, 5, 999.00m));
        sourceLines[0] = (1, 100, 249.90m);

        Assert.Equal(itemCountBefore, order.Items.Count);
        Assert.Equal(totalBefore, order.TotalAmount);
    }

    // Not tied to a specific collection type or a specific exception: ReadOnlyCollection<T>
    // (what Items happens to return today) actually implements ICollection<T> for interop, with
    // Add/Remove/Clear throwing at call time rather than being absent -- so "does it implement
    // ICollection<T>" is the wrong question. This checks the behavior that actually matters: if
    // the returned object exposes a mutating member at all, using it must fail, whatever the
    // concrete type or exception type turns out to be.
    [Fact]
    public void Items_CannotBeMutatedThroughAnyExposedInterface()
    {
        var order = Order.Create(new (int, int, decimal)[] { (1, 1, 10m) });
        var countBefore = order.Items.Count;

        if (order.Items is ICollection<OrderItem> mutable)
        {
            Assert.ThrowsAny<Exception>(() => mutable.Add(order.Items.First()));
        }

        Assert.Equal(countBefore, order.Items.Count);
    }

    // Same idea applied to the line items themselves: no public setter means no external code --
    // whatever type Items happens to return -- can reach in and change a line's quantity/price
    // after the fact.
    [Fact]
    public void OrderItemProperties_HaveNoPublicSetters()
    {
        var properties = typeof(OrderItem).GetProperties();

        Assert.All(properties, property => Assert.True(
            property.SetMethod is null || !property.SetMethod.IsPublic,
            $"{property.Name} must not have a public setter."));
    }

    [Fact]
    public void TotalAmount_IsComputedFromItems_NotStoredIndependently()
    {
        var order = Order.Create(new (int, int, decimal)[] { (1, 3, 249.90m) });

        Assert.Equal(3 * 249.90m, order.TotalAmount);
        Assert.Equal(order.Items.Single().LineTotal, order.TotalAmount);
    }
}
