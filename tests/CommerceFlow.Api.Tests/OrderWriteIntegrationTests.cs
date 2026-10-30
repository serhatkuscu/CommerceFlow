using System.Net;
using System.Net.Http.Json;
using CommerceFlow.Database.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace CommerceFlow.Api.Tests;

public sealed class OrderWriteDatabase : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        Environment.SetEnvironmentVariable("CommerceFlowEnvironment", "Test");
        if (!MigrationRunner.Migrate(ConnectionString))
            throw new InvalidOperationException("Migration failed.");
        ResetData();
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    public void ResetData()
    {
        if (!MigrationRunner.ResetData(ConnectionString))
            throw new InvalidOperationException("Seed reset failed.");
    }
}

// Deliberately separate from OrderWriter under test -- a bug in OrderWriter's own ADO.NET code
// can't hide itself from its own verification this way.
internal static class WriteTestDb
{
    public static int GetStockQuantity(string connectionString, int productId)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(
            "SELECT StockQuantity FROM dbo.Products WHERE ProductId = @ProductId", connection);
        command.Parameters.AddWithValue("@ProductId", productId);
        connection.Open();
        return (int)command.ExecuteScalar()!;
    }

    public static int GetOrderCount(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Orders", connection);
        connection.Open();
        return (int)command.ExecuteScalar()!;
    }

    public static decimal GetStoredUnitPrice(string connectionString, int orderId, int productId)
    {
        using var connection = new SqlConnection(connectionString);
        using var command = new SqlCommand(
            "SELECT UnitPrice FROM dbo.OrderItems WHERE OrderId = @OrderId AND ProductId = @ProductId", connection);
        command.Parameters.AddWithValue("@OrderId", orderId);
        command.Parameters.AddWithValue("@ProductId", productId);
        connection.Open();
        return (decimal)command.ExecuteScalar()!;
    }
}

public class OrderWriteIntegrationTests : IClassFixture<OrderWriteDatabase>, IDisposable
{
    private readonly OrderWriteDatabase database;
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public OrderWriteIntegrationTests(OrderWriteDatabase database)
    {
        this.database = database;
        database.ResetData(); // deterministic seed + zero orders, matching OrderApiTests' pattern.
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["COMMERCEFLOW_DB_CONNECTION"] = database.ConnectionString }));
        });
        client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task CreateOrder_ValidRequest_PersistsOrderAndDecrementsStock()
    {
        var stockBefore = WriteTestDb.GetStockQuantity(database.ConnectionString, 1);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 2 }, new { productId = 3, quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orderId = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("orderId").GetInt32();

        var getResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        Assert.Equal(stockBefore - 2, WriteTestDb.GetStockQuantity(database.ConnectionString, 1));
        Assert.Equal(1, WriteTestDb.GetOrderCount(database.ConnectionString));
        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task ClientSuppliedUnitPrice_IsIgnored_CatalogPriceIsStoredInstead()
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 1, unitPrice = 0.01m } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orderId = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
            .GetProperty("orderId").GetInt32();

        // 249.90 is product 1's seeded catalog price (SeedData/0003_SeedProducts.sql) -- proof
        // the attempted 0.01 client price never reached the stored procedure at all.
        Assert.Equal(249.90m, WriteTestDb.GetStoredUnitPrice(database.ConnectionString, orderId, 1));
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_Returns400WithoutWritingAnything()
    {
        var response = await client.PostAsJsonAsync(
            "/api/orders", new { customerId = 1, items = Array.Empty<object>() });

        await AssertBadRequest(response, "Order must contain at least one item.");
        Assert.Equal(0, WriteTestDb.GetOrderCount(database.ConnectionString));
    }

    [Fact]
    public async Task CreateOrder_ZeroQuantity_Returns400WithoutTouchingStock()
    {
        var stockBefore = WriteTestDb.GetStockQuantity(database.ConnectionString, 1);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 0 } }
        });

        await AssertBadRequest(response, "Quantity must be greater than zero for product 1.");
        Assert.Equal(stockBefore, WriteTestDb.GetStockQuantity(database.ConnectionString, 1));
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_Returns400()
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 999,
            items = new[] { new { productId = 1, quantity = 1 } }
        });

        await AssertBadRequest(response, "Customer 999 not found.");
    }

    [Fact]
    public async Task CreateOrder_UnknownProduct_Returns400()
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 999, quantity = 1 } }
        });

        await AssertBadRequest(response, "Product 999 not found.");
    }

    [Fact]
    public async Task CreateOrder_InsufficientStock_IsAllOrNothing()
    {
        // Product 5 is the seeded low-stock item (StockQuantity = 1, SeedData/0003_SeedProducts.sql).
        var stockBefore = WriteTestDb.GetStockQuantity(database.ConnectionString, 1);

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 1 }, new { productId = 5, quantity = 5 } }
        });

        await AssertBadRequest(response, "Insufficient stock for product 5.");

        // The line that WOULD have succeeded (product 1) must not have been decremented either --
        // proves the stored procedure's transaction rolled back, not just that an error surfaced.
        Assert.Equal(stockBefore, WriteTestDb.GetStockQuantity(database.ConnectionString, 1));
        Assert.Equal(0, WriteTestDb.GetOrderCount(database.ConnectionString));
    }

    private static async Task AssertBadRequest(HttpResponseMessage response, string expectedDetail)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal(expectedDetail, body.GetProperty("detail").GetString());
    }
}
