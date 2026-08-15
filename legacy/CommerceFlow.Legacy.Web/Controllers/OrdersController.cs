using CommerceFlow.Legacy.BLL;
using CommerceFlow.Legacy.Models;
using CommerceFlow.Legacy.Web.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace CommerceFlow.Legacy.Web.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderManager _orderManager = new();

    // async Task<IActionResult> + CancellationToken is the modern ASP.NET Core signature, kept
    // deliberately even though OrderManager/OrderDataAccess underneath are fully synchronous
    // ADO.NET (see CommerceFlow.Legacy.DAL -- debt item #9). Once a request reaches the database
    // call it cannot actually be aborted, so there is no meaningful await here; that absence is
    // the honest signal, not something to paper over with Task.Run, which would just burn an
    // extra thread-pool thread for no benefit -- the opposite of what "async" is supposed to buy
    // you on a web server. A real fix means an async DAL, which belongs to the eventual
    // migration, not this milestone.
#pragma warning disable CS1998
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var orderId = _orderManager.CreateOrder(request.CustomerId, request.Items);
            var order = _orderManager.GetOrderById(orderId)!;
            return Ok(ApiEnvelope<OrderResponse>.Ok(OrderResponse.FromOrder(order)));
        }
        catch (BusinessRuleException ex)
        {
            return Ok(ApiEnvelope<OrderResponse>.Fail(ex.Message));
        }
        catch (Exception)
        {
            // Not logged here -- matches the legacy debt of inconsistent logging (item #7).
            // A real logging story starts at M1's target skeleton, not this milestone.
            return Ok(ApiEnvelope<OrderResponse>.Fail("An unexpected error occurred."));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetOrderById(int id, CancellationToken cancellationToken)
    {
        try
        {
            var order = _orderManager.GetOrderById(id);
            if (order is null)
            {
                // HTTP 200, not 404 -- known legacy quirk (AC5), characterized as-is.
                return Ok(ApiEnvelope<OrderResponse>.Fail("Order not found."));
            }

            return Ok(ApiEnvelope<OrderResponse>.Ok(OrderResponse.FromOrder(order)));
        }
        catch (Exception)
        {
            return Ok(ApiEnvelope<OrderResponse>.Fail("An unexpected error occurred."));
        }
    }
#pragma warning restore CS1998
}
