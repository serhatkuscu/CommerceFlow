namespace CommerceFlow.Modules.Ordering.Application;

public interface IOrderWriter
{
    Task<CreateOrderResult> CreateAsync(CreateOrderRequest request, CancellationToken ct);
}
