namespace CommerceFlow.Modules.Ordering.Application;

public interface IOrderQueries
{
    Task<OrderDetailsDto?> GetByIdAsync(int orderId, CancellationToken ct);
}
