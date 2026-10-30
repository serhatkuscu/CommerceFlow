using CommerceFlow.Modules.Inventory.Domain;

namespace CommerceFlow.Modules.Inventory.Domain.Tests;

public class StockTests
{
    [Fact]
    public void Create_WithValidQuantity_Succeeds()
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 10);

        Assert.Equal(1, stock.ProductId);
        Assert.Equal(10, stock.QuantityOnHand);
    }

    [Fact]
    public void Create_WithZeroQuantity_Succeeds()
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 0);

        Assert.Equal(0, stock.QuantityOnHand);
    }

    // CK_Products_StockQuantity CHECK (StockQuantity >= 0).
    [Fact]
    public void Create_WithNegativeQuantity_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Stock.Create(productId: 1, quantityOnHand: -1));

        Assert.Equal(
            "Quantity on hand cannot be negative. (Parameter 'quantityOnHand')", ex.Message);
    }

    [Fact]
    public void TryReserve_WithSufficientStock_DecrementsAndReturnsTrue()
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 10);

        var reserved = stock.TryReserve(4);

        Assert.True(reserved);
        Assert.Equal(6, stock.QuantityOnHand);
    }

    [Fact]
    public void TryReserve_WithExactlyAvailableQuantity_DecrementsToZero()
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 5);

        var reserved = stock.TryReserve(5);

        Assert.True(reserved);
        Assert.Equal(0, stock.QuantityOnHand);
    }

    // Mirrors usp_CreateOrder's conditional UPDATE + @@ROWCOUNT = 0 check (THROW 51002) -- an
    // expected business outcome, not a caller error, so it returns false rather than throwing.
    [Fact]
    public void TryReserve_WithInsufficientStock_ReturnsFalseAndDoesNotChangeQuantity()
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 3);

        var reserved = stock.TryReserve(4);

        Assert.False(reserved);
        Assert.Equal(3, stock.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryReserve_WithZeroOrNegativeQuantity_Throws(int quantity)
    {
        var stock = Stock.Create(productId: 1, quantityOnHand: 10);

        var ex = Assert.Throws<ArgumentException>(() => stock.TryReserve(quantity));

        Assert.Equal(
            "Reservation quantity must be greater than zero. (Parameter 'quantity')", ex.Message);
        Assert.Equal(10, stock.QuantityOnHand);
    }
}
