namespace CommerceFlow.Modules.Ordering.Application;

public sealed record OrderDetailsDto(int OrderId, int Status, decimal TotalAmount, List<OrderItemDto> Items);

public sealed record OrderItemDto(int ProductId, int Quantity, decimal UnitPrice, decimal LineTotal);
