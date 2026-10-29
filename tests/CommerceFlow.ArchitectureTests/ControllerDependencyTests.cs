using CommerceFlow.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;

namespace CommerceFlow.ArchitectureTests;

public class ControllerDependencyTests
{
    [Fact]
    public void Controllers_DoNotDependOnInfrastructure()
    {
        var assembly = typeof(OrdersController).Assembly;
        Assert.Contains(assembly.GetTypes(), t => typeof(ControllerBase).IsAssignableFrom(t));
        var result = Types.InAssembly(assembly).That().Inherit(typeof(ControllerBase))
            .ShouldNot().HaveDependencyOn("CommerceFlow.Modules.Ordering.Infrastructure").GetResult();
        Assert.True(result.IsSuccessful);
    }
}
