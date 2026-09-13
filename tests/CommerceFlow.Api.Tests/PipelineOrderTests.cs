using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CommerceFlow.Api.Tests;

// Real full-pipeline test via WebApplicationFactory -- unlike CorrelationIdMiddlewareTests (which
// instantiate the middleware directly), this exercises the actual Program.cs pipeline ordering,
// including Swagger. It exists specifically to prevent a regression of the bug this slice fixed:
// Swagger's middleware can short-circuit the pipeline (respond without calling next()), so any
// middleware registered after it never runs for /swagger/* -- correlation ID must be registered
// before Swagger, not after, for every HTTP request to get the same observability behavior.
public class PipelineOrderTests
{
    [Fact]
    public async Task SwaggerJsonResponse_AlsoReceivesCorrelationIdHeader()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }
}
