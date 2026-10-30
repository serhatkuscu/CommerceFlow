namespace CommerceFlow.Modules.Ordering.Application;

// Mirrors the five business errors usp_CreateOrder itself throws (51000-51004). All five are
// expected, validated outcomes the caller sent bad/stale input -- never a reason to 500.
public enum OrderCreationError
{
    None,
    EmptyOrder,
    InvalidQuantity,
    CustomerNotFound,
    ProductNotFound,
    InsufficientStock,
}

public sealed record CreateOrderResult(int? OrderId, OrderCreationError Error, string? ErrorMessage)
{
    public bool IsSuccess => Error == OrderCreationError.None;

    public static CreateOrderResult Success(int orderId) => new(orderId, OrderCreationError.None, null);

    public static CreateOrderResult Failure(OrderCreationError error, string message) =>
        new(null, error, message);
}
