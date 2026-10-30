using CommerceFlow.Modules.Ordering.Application;
using Microsoft.AspNetCore.Mvc;

namespace CommerceFlow.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderQueries queries, CreateOrderUseCase createOrder) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailsDto>> GetById(int id, CancellationToken ct)
    {
        var order = await queries.GetByIdAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var result = await createOrder.CreateAsync(request, ct);

        // All five usp_CreateOrder business errors are expected, validated outcomes -- never a
        // 500. They are deliberately not split across different status codes (400 vs 404 vs
        // 409): none of them represents a server fault, and the Problem Details message already
        // names the exact reason, so one uniform "the request is invalid" status is the simplest
        // design that is still honest to the client.
        if (!result.IsSuccess)
        {
            return Problem(detail: result.ErrorMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.OrderId }, new { orderId = result.OrderId });
    }
}
