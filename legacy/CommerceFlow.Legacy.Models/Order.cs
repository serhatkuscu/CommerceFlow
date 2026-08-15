namespace CommerceFlow.Legacy.Models;

public class Order
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public int OrderStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsExportedToErp { get; set; }
    public DateTime? ExportedDate { get; set; }
    public int ErpExportAttempts { get; set; }
    public DateTime CreatedDate { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}
