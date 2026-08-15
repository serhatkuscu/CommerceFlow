using CommerceFlow.Legacy.Models;

namespace CommerceFlow.Legacy.Web.Dtos;

public class OrderItemResponse
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderResponse
{
    public int OrderId { get; set; }
    public int Status { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();

    public static OrderResponse FromOrder(Order order) => new()
    {
        OrderId = order.OrderId,
        Status = order.OrderStatus,
        StatusText = OrderStatus.ToDisplayName(order.OrderStatus),
        TotalAmount = order.TotalAmount,
        Items = order.Items.Select(i => new OrderItemResponse
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice,
            LineTotal = i.LineTotal
        }).ToList()
    };
}
