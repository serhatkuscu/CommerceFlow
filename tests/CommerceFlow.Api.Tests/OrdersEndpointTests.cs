using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CommerceFlow.Modules.Ordering.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommerceFlow.Api.Tests;

public class OrdersEndpointTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_ReturnsDtoOr404(bool exists)
    {
        var expected = new OrderDetailsDto(42, 1, 25.50m, [new(7, 2, 12.75m, 25.50m)]);
        var queries = new StubQueries(exists ? expected : null);
        await using var factory = CreateFactory(queries);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/orders/42");
        Assert.Equal(exists ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(42, queries.RequestedId);
        Assert.True(queries.Token.CanBeCanceled);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        if (exists)
        {
            var actual = (await response.Content.ReadFromJsonAsync<OrderDetailsDto>())!;
            Assert.Equal(expected.OrderId, actual.OrderId);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.TotalAmount, actual.TotalAmount);
            Assert.Equal(expected.Items, actual.Items);
        }
    }

    [Fact]
    public async Task QueryFailure_UsesGlobalExceptionHandlerAndCorrelationId()
    {
        await using var factory = CreateFactory(queries: new StubQueries(null, true));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders/42");
        request.Headers.Add("X-Correlation-ID", "orders-regression");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("orders-regression", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("orders-regression", body.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("private database error", body.ToString());
    }

    [Fact]
    public async Task Create_Success_Returns201WithLocationAndCorrelationId()
    {
        var writer = new StubWriter(CreateOrderResult.Success(99));
        await using var factory = CreateFactory(writer: writer);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(1, [new CreateOrderLine(1, 2)]))
        };
        request.Headers.Add("X-Correlation-ID", "orders-create-success");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith("/api/orders/99", response.Headers.Location!.ToString());
        Assert.Equal("orders-create-success", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(99, body.GetProperty("orderId").GetInt32());
        Assert.Equal(1, writer.ReceivedRequest!.CustomerId);
    }

    [Fact]
    public async Task Create_BusinessError_Returns400ProblemDetailsWithCorrelationId()
    {
        var writer = new StubWriter(
            CreateOrderResult.Failure(OrderCreationError.InsufficientStock, "Insufficient stock for product 5."));
        await using var factory = CreateFactory(writer: writer);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new CreateOrderRequest(1, [new CreateOrderLine(5, 999)]))
        };
        request.Headers.Add("X-Correlation-ID", "orders-create-business-error");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "orders-create-business-error", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Insufficient stock for product 5.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_ClientSuppliedUnitPrice_IsNotBoundAnywhere()
    {
        // CreateOrderLine has no UnitPrice property at all -- this proves a client attempting to
        // send one has nowhere for it to land; the stub never sees a price, only ProductId/
        // Quantity. The DB-level proof (stored price is the catalog price, not this one) is
        // OrderWriteIntegrationTests.ClientSuppliedUnitPrice_IsIgnored_CatalogPriceIsStoredInstead.
        var writer = new StubWriter(CreateOrderResult.Success(1));
        await using var factory = CreateFactory(writer: writer);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            customerId = 1,
            items = new[] { new { productId = 1, quantity = 1, unitPrice = 0.01m } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receivedLine = Assert.Single(writer.ReceivedRequest!.Items!);
        Assert.Equal(1, receivedLine.ProductId);
        Assert.Equal(1, receivedLine.Quantity);
        Assert.DoesNotContain("unitPrice", receivedLine.GetType().GetProperties().Select(p => p.Name));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IOrderQueries? queries = null, IOrderWriter? writer = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                // OrdersController has one constructor needing both IOrderQueries and
                // CreateOrderUseCase/IOrderWriter -- DI resolves every parameter to construct it
                // regardless of which action runs, so both must always be stubbed here, not just
                // whichever one a given test cares about.
                services.RemoveAll<IOrderQueries>();
                services.AddSingleton(queries ?? new StubQueries(null));

                services.RemoveAll<IOrderWriter>();
                services.AddSingleton(writer ?? new StubWriter(CreateOrderResult.Success(0)));
            });
        });

    private sealed class StubQueries(OrderDetailsDto? result, bool fail = false) : IOrderQueries
    {
        public int RequestedId { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<OrderDetailsDto?> GetByIdAsync(int orderId, CancellationToken ct)
        {
            RequestedId = orderId;
            Token = ct;
            if (fail) throw new InvalidOperationException("private database error");
            return Task.FromResult(result);
        }
    }

    private sealed class StubWriter(CreateOrderResult result) : IOrderWriter
    {
        public CreateOrderRequest? ReceivedRequest { get; private set; }
        public Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken ct)
        {
            ReceivedRequest = request;
            return Task.FromResult(result);
        }
    }
}
