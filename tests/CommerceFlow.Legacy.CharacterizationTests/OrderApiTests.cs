using System.Net.Http.Json;
using CommerceFlow.Legacy.Web.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommerceFlow.Legacy.CharacterizationTests;

[Collection("Database")]
public class OrderApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly DatabaseFixture _database;
    private readonly HttpClient _client;

    public OrderApiTests(DatabaseFixture database, WebApplicationFactory<Program> factory)
    {
        _database = database;
        _database.ResetData();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_HappyPath_PersistsOrderAndDecrementsStock()
    {
        var stockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 1);

        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 2 }, new { productId = 3, quantity = 1 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.True(body!.Success);
        Assert.Equal(849.30m, body.Data!.TotalAmount);
        Assert.Equal(2, body.Data.Items.Count);

        // DB state, not just the HTTP response.
        Assert.Equal(stockBefore - 2, TestDb.GetStockQuantity(_database.ConnectionString, 1));
        Assert.Equal(2, TestDb.GetOrderItemCount(_database.ConnectionString, body.Data.OrderId));
        Assert.Equal(1, TestDb.GetOrderCount(_database.ConnectionString));
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_IsAllOrNothing()
    {
        var stockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 1);

        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 2,
            items = new[] { new { productId = 1, quantity = 1 }, new { productId = 5, quantity = 5 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.False(body!.Success);
        Assert.Equal("Insufficient stock for product 5.", body.Message);

        // The line that WOULD have succeeded (product 1) must not have been decremented either --
        // proves rollback, not just the error message.
        Assert.Equal(stockBefore, TestDb.GetStockQuantity(_database.ConnectionString, 1));
        Assert.Equal(0, TestDb.GetOrderCount(_database.ConnectionString));
    }

    [Fact]
    public async Task CreateOrder_DuplicateProductLine_KeepsSeparateRowsButDecrementsOnceByTheSum()
    {
        var stockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 2);

        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 3,
            items = new[] { new { productId = 2, quantity = 1 }, new { productId = 2, quantity = 2 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.True(body!.Success);
        Assert.Equal(2, body.Data!.Items.Count); // AC8: not merged into one line

        Assert.Equal(stockBefore - 3, TestDb.GetStockQuantity(_database.ConnectionString, 2));
        Assert.Equal(2, TestDb.GetOrderItemCount(_database.ConnectionString, body.Data.OrderId));
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_IsRejectedWithoutWritingAnything()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new { customerId = 1, items = Array.Empty<object>() });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.False(body!.Success);
        Assert.Equal("Order must contain at least one item.", body.Message);
        Assert.Equal(0, TestDb.GetOrderCount(_database.ConnectionString));
    }

    [Fact]
    public async Task CreateOrder_ZeroQuantity_IsRejectedWithoutTouchingStock()
    {
        var stockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 1);

        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 0 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.False(body!.Success);
        Assert.Equal("Quantity must be greater than zero for product 1.", body.Message);
        Assert.Equal(stockBefore, TestDb.GetStockQuantity(_database.ConnectionString, 1));
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 999,
            items = new[] { new { productId = 1, quantity = 1 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.False(body!.Success);
        Assert.Equal("Customer 999 not found.", body.Message);
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 999, quantity = 1 } }
        });

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();
        Assert.False(body!.Success);
        Assert.Equal("Product 999 not found.", body.Message);
    }

    [Fact]
    public async Task GetOrderById_ExistingOrder_ReturnsIt()
    {
        var create = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 4, quantity = 1 } }
        });
        var created = await create.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();

        var response = await _client.GetAsync($"/api/orders/{created!.Data!.OrderId}");
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(body!.Success);
        Assert.Equal(created.Data.OrderId, body.Data!.OrderId);
    }

    [Fact]
    public async Task GetOrderById_MissingOrder_ReturnsHttp200NotFoundEnvelope()
    {
        // AC5: known legacy quirk, characterized as-is -- HTTP 200, not 404.
        var response = await _client.GetAsync("/api/orders/999999");
        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.False(body!.Success);
        Assert.Equal("Order not found.", body.Message);
    }
}
