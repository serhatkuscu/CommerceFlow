using CommerceFlow.Modules.Ordering.Application;
using Microsoft.AspNetCore.Mvc;

namespace CommerceFlow.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderQueries queries) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailsDto>> GetById(int id, CancellationToken ct)
    {
        var order = await queries.GetByIdAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }
}
