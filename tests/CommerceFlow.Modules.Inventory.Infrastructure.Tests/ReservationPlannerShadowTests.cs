using CommerceFlow.Database.Migrations;
using CommerceFlow.Legacy.BLL;
using CommerceFlow.Legacy.Models;
using CommerceFlow.Modules.Inventory.Application;
using CommerceFlow.Modules.Inventory.Domain;
using Testcontainers.MsSql;

namespace CommerceFlow.Modules.Inventory.Infrastructure.Tests;

public sealed class InventoryShadowDatabase : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        // OrderManager/OrderDataAccess read this from the process environment, not
        // IConfiguration -- matching production and every other fixture in this repo that needs
        // to go through the real order-creation contract, rather than inventing a different
        // wiring mechanism just for this test.
        Environment.SetEnvironmentVariable("COMMERCEFLOW_DB_CONNECTION", ConnectionString);
        Environment.SetEnvironmentVariable("CommerceFlowEnvironment", "Test");

        if (!MigrationRunner.Migrate(ConnectionString))
            throw new InvalidOperationException("Migration failed.");

        ResetData();
    }

    public void ResetData()
    {
        if (!MigrationRunner.ResetData(ConnectionString))
            throw new InvalidOperationException("Seed reset failed.");
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}

// "Shadow mode": ReservationPlanner/StockReader never run in the production create-order path
// (see Stock.cs, CreateOrderUseCase.cs). These tests exist to prove the C# decision agrees with
// usp_CreateOrder's real, deployed decision for the same inputs -- not to replace it. Nothing
// here is ever written back; StockReader snapshots are read once, before the real order exists.
// Seeded stock (SeedData/0003_SeedProducts.sql): ProductId 1 = 100, 3 = 30, 5 = 1.
public class ReservationPlannerShadowTests : IClassFixture<InventoryShadowDatabase>
{
    private readonly InventoryShadowDatabase database;

    public ReservationPlannerShadowTests(InventoryShadowDatabase database)
    {
        this.database = database;
        database.ResetData();
    }

    [Fact]
    public async Task SufficientStockWithDuplicateLine_PlannerPredictionMatchesRealStoredProcedureOutcome()
    {
        var reader = new StockReader(database.ConnectionString);

        // AC8: duplicate ProductId lines are not merged into one order row, but usp_CreateOrder
        // still decrements stock once by their SUM -- these two lines for product 1 (2 + 3 = 5).
        var lines = new[] { new ReservationLine(1, 2), new ReservationLine(1, 3), new ReservationLine(3, 4) };

        var stock1 = (await reader.GetByProductIdAsync(1, CancellationToken.None))!;
        var stock3 = (await reader.GetByProductIdAsync(3, CancellationToken.None))!;
        var stockByProductId = new Dictionary<int, Stock> { [1] = stock1, [3] = stock3 };

        var plan = ReservationPlanner.TryPlan(stockByProductId, lines);

        Assert.True(plan.Succeeded);
        var predictedStock1 = stock1.QuantityOnHand; // mutated in place by TryPlan
        var predictedStock3 = stock3.QuantityOnHand;

        // Now the real thing: same lines, through the sanctioned contract, into usp_CreateOrder.
        new OrderManager().CreateOrder(customerId: 1, new List<OrderLineRequest>
        {
            new() { ProductId = 1, Quantity = 2 },
            new() { ProductId = 1, Quantity = 3 },
            new() { ProductId = 3, Quantity = 4 },
        });

        Assert.Equal(predictedStock1, await ReadRealStockQuantity(1));
        Assert.Equal(predictedStock3, await ReadRealStockQuantity(3));
    }

    [Fact]
    public async Task InsufficientStock_PlannerPredictsFailureOnSameProductRealStoredProcedureRejects()
    {
        var reader = new StockReader(database.ConnectionString);

        // Mirrors Legacy.CharacterizationTests.OrderApiTests.CreateOrder_InsufficientStock_IsAllOrNothing:
        // product 1 (ample stock) + product 5 (seeded at 1, request 5 -- not enough).
        var lines = new[] { new ReservationLine(1, 1), new ReservationLine(5, 5) };

        var stock1 = (await reader.GetByProductIdAsync(1, CancellationToken.None))!;
        var stock5 = (await reader.GetByProductIdAsync(5, CancellationToken.None))!;
        var stockBefore1 = stock1.QuantityOnHand;
        var stockByProductId = new Dictionary<int, Stock> { [1] = stock1, [5] = stock5 };

        var plan = ReservationPlanner.TryPlan(stockByProductId, lines);

        Assert.False(plan.Succeeded);
        Assert.Equal(5, plan.InsufficientProductId);

        var ex = Assert.Throws<BusinessRuleException>(() => new OrderManager().CreateOrder(
            customerId: 1,
            new List<OrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 1 },
                new() { ProductId = 5, Quantity = 5 },
            }));
        Assert.Equal("Insufficient stock for product 5.", ex.Message);

        // All-or-nothing: the line that would have succeeded (product 1) must be untouched too --
        // both the real transaction and our planner's own prediction agree on this.
        Assert.Equal(stockBefore1, await ReadRealStockQuantity(1));
    }

    private async Task<int> ReadRealStockQuantity(int productId)
    {
        var stock = await new StockReader(database.ConnectionString).GetByProductIdAsync(productId, CancellationToken.None);
        return stock!.QuantityOnHand;
    }
}
