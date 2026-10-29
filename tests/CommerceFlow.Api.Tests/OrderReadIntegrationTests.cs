using System.Net;
using System.Net.Http.Json;
using CommerceFlow.Database.Migrations;
using CommerceFlow.Legacy.BLL;
using CommerceFlow.Legacy.Models;
using CommerceFlow.Modules.Ordering.Application;
using CommerceFlow.Modules.Ordering.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;

namespace CommerceFlow.Api.Tests;

public sealed class OrderReadDatabase : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public string ConnectionString => container.GetConnectionString();
    public int OrderId { get; private set; }

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        // OrderManager/OrderDataAccess read their connection from this process environment
        // variable, not from IConfiguration -- matching how production and
        // Legacy.CharacterizationTests.DatabaseFixture both wire it, instead of inventing a
        // different mechanism just for this fixture.
        Environment.SetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("CommerceFlowEnvironment", "Test");

        if (!MigrationRunner.Migrate(ConnectionString))
            throw new InvalidOperationException("Migration failed.");
        if (!MigrationRunner.ResetData(ConnectionString))
            throw new InvalidOperationException("Seed reset failed.");

        // Created through the existing order-creation contract (OrderManager -> usp_CreateOrder),
        // not a hand-rolled INSERT -- this fixture can never drift from what production actually
        // writes. CustomerId 1 / ProductId 1+3 are the same deterministic seed rows OrderApiTests
        // already relies on (database/.../SeedData), so 849.30 here matches that suite exactly.
        OrderId = new OrderManager().CreateOrder(customerId: 1, new List<OrderLineRequest>
        {
            new() { ProductId = 1, Quantity = 2 },
            new() { ProductId = 3, Quantity = 1 }
        });
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}

public class OrderReadIntegrationTests(OrderReadDatabase database) : IClassFixture<OrderReadDatabase>
{
    [Fact]
    public async Task Query_ReadsBothResultSetsAndReturnsNullForMissingOrder()
    {
        var queries = new OrderQueries(database.ConnectionString);
        AssertOrder(await queries.GetByIdAsync(database.OrderId, CancellationToken.None));
        Assert.Null(await queries.GetByIdAsync(int.MaxValue, CancellationToken.None));
    }

    [Fact]
    public async Task Endpoint_ReadsRealDatabaseWithCorrelationHeaderAnd404()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["COMMERCEFLOW_DB_CONNECTION"] = database.ConnectionString }));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "real-order-read");
        var response = await client.GetAsync($"/api/orders/{database.OrderId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("real-order-read", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        AssertOrder(await response.Content.ReadFromJsonAsync<OrderDetailsDto>());
        var missing = await client.GetAsync($"/api/orders/{int.MaxValue}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("real-order-read", Assert.Single(missing.Headers.GetValues("X-Correlation-ID")));
    }

    private void AssertOrder(OrderDetailsDto? order)
    {
        Assert.NotNull(order);
        Assert.Equal(database.OrderId, order.OrderId);
        Assert.Equal(0, order.Status); // usp_CreateOrder always inserts OrderStatus 0 (Pending).
        Assert.Equal(849.30m, order.TotalAmount);
        Assert.Collection(order.Items,
            item =>
            {
                Assert.Equal(1, item.ProductId);
                Assert.Equal(2, item.Quantity);
                Assert.Equal(249.90m, item.UnitPrice);
                Assert.Equal(499.80m, item.LineTotal);
            },
            item =>
            {
                Assert.Equal(3, item.ProductId);
                Assert.Equal(1, item.Quantity);
                Assert.Equal(349.50m, item.UnitPrice);
                Assert.Equal(349.50m, item.LineTotal);
            });
    }
}
