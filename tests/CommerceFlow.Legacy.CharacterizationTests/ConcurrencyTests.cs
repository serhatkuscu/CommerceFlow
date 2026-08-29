using System.Net.Http.Json;
using CommerceFlow.Legacy.Web.Dtos;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommerceFlow.Legacy.CharacterizationTests;

// Characterization tests: verify stock and order integrity hold under concurrent HTTP load.
// These do NOT prove that a real deadlock occurred, and they do NOT prove that canonical
// product-lock ordering prevents deadlocks -- that specific claim is a structural property of
// usp_CreateOrder's cursor (see StoredProcedureContractTests.cs), not something a race-timing
// test can meaningfully confirm or deny: a local SQL Server call can complete in single-digit
// milliseconds, so launching requests "concurrently" does not guarantee any two of them ever
// contended for the same row lock. What these two tests DO prove with certainty: under this
// load shape, stock never goes negative and no order is ever partially written, regardless of
// whether any two requests' database work actually overlapped in time.
//
// KNOWN GAP (documented, not implemented): there is no test verifying that once the 1205 retry
// budget in OrderDataAccess is exhausted, the client receives the generic "An unexpected error
// occurred" message rather than raw SQL exception text. Building one requires either (a) a real,
// precisely synchronized SQL Server deadlock forced through an external harness reaching the
// full HTTP stack -- exactly the kind of large, fragile, ad-hoc infrastructure this milestone is
// avoiding -- or (b) adding DI/interface seams to OrderDataAccess/OrderManager/OrdersController
// purely for testability, which conflicts with the deliberate no-DI legacy design (see
// OrderDataAccess.cs). Neither is proportionate here; revisit once M1's target skeleton
// introduces real seams.
[Collection("Database")]
public class ConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly DatabaseFixture _database;
    private readonly HttpClient _client;

    public ConcurrencyTests(DatabaseFixture database, WebApplicationFactory<Program> factory)
    {
        _database = database;
        _database.ResetData();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_ConcurrentOrdersWithCrossingProductOrder_DoNotCorruptStockOrOrders()
    {
        const int concurrentOrders = 30;
        var product1StockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 1);
        var product2StockBefore = TestDb.GetStockQuantity(_database.ConnectionString, 2);

        // Half submit [Product1, Product2], half submit [Product2, Product1]. This is the
        // client-order crossing that, without canonical lock ordering inside usp_CreateOrder,
        // could in principle contribute to a deadlock -- it does not guarantee that any two
        // requests' transactions genuinely overlapped in time (see the class-level comment).
        // What it verifies with certainty is the outcome, not the mechanism.
        var tasks = Enumerable.Range(0, concurrentOrders).Select(i =>
        {
            var itemsAscending = new object[] { new { productId = 1, quantity = 1 }, new { productId = 2, quantity = 1 } };
            var itemsDescending = new object[] { new { productId = 2, quantity = 1 }, new { productId = 1, quantity = 1 } };
            var items = i % 2 == 0 ? itemsAscending : itemsDescending;

            return _client.PostAsJsonAsync("/api/orders", new { customerId = 1, items });
        }).ToArray();

        var responses = await Task.WhenAll(tasks);
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>()));

        var succeeded = bodies.Count(b => b!.Success);
        var failed = bodies.Where(b => !b!.Success).ToList();

        // Stock never goes negative, under any outcome.
        var product1StockAfter = TestDb.GetStockQuantity(_database.ConnectionString, 1);
        var product2StockAfter = TestDb.GetStockQuantity(_database.ConnectionString, 2);
        Assert.True(product1StockAfter >= 0);
        Assert.True(product2StockAfter >= 0);

        // Both products have generous stock (100, 50) relative to 30 concurrent 1-unit orders,
        // so every request should succeed. If any failed, it must be the documented, bounded
        // deadlock-exhaustion path (AC11) -- surfaced here with its actual message, not
        // swallowed -- never silent data corruption.
        if (failed.Count > 0)
        {
            var reasons = string.Join("; ", failed.Select(b => b!.Message));
            Assert.Fail($"{failed.Count}/{concurrentOrders} concurrent orders failed unexpectedly: {reasons}");
        }

        Assert.Equal(concurrentOrders, succeeded);
        Assert.Equal(product1StockBefore - concurrentOrders, product1StockAfter);
        Assert.Equal(product2StockBefore - concurrentOrders, product2StockAfter);

        // No partial writes: every successful response has a real, fully-formed order behind it,
        // and nothing else snuck in.
        Assert.Equal(concurrentOrders, TestDb.GetOrderCount(_database.ConnectionString));
    }

    [Fact]
    public async Task CreateOrder_ConcurrentOrdersForScarceStock_NeverOversells()
    {
        // Verifies the oversell-prevention invariant holds under concurrent HTTP load. This does
        // not require genuine simultaneous contention to pass -- the same outcome (exactly one
        // winner, stock never negative) would also occur if all requests happened to run in
        // strict sequence. Either way, the invariant itself is what's being guarded here, and
        // that's the thing worth catching a regression in.
        const int concurrentOrders = 10; // seeded stock for product 5 is 1 -- at most one can win.
        var tasks = Enumerable.Range(0, concurrentOrders).Select(_ =>
            _client.PostAsJsonAsync("/api/orders", new
            {
                customerId = 1,
                items = new[] { new { productId = 5, quantity = 1 } }
            })).ToArray();

        var responses = await Task.WhenAll(tasks);
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<ApiEnvelope<OrderResponse>>()));

        var succeeded = bodies.Count(b => b!.Success);
        var stockAfter = TestDb.GetStockQuantity(_database.ConnectionString, 5);

        Assert.True(stockAfter >= 0); // the hard invariant: never negative, no matter what
        Assert.Equal(1, succeeded); // exactly one order could possibly have had stock for
        Assert.Equal(0, stockAfter);
        Assert.Equal(1, TestDb.GetOrderCount(_database.ConnectionString));

        var insufficientStockFailures = bodies.Count(b => !b!.Success && b.Message == "Insufficient stock for product 5.");
        Assert.Equal(concurrentOrders - 1, insufficientStockFailures);
    }
}
