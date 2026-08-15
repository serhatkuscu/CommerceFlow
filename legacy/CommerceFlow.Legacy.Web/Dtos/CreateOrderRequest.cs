using CommerceFlow.Legacy.Models;

namespace CommerceFlow.Legacy.Web.Dtos;

public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public List<OrderLineRequest> Items { get; set; } = new();
}
