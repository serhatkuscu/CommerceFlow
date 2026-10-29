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
        await using var factory = CreateFactory(new StubQueries(null, true));
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

    private static WebApplicationFactory<Program> CreateFactory(IOrderQueries queries) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IOrderQueries>();
                services.AddSingleton(queries);
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
}
