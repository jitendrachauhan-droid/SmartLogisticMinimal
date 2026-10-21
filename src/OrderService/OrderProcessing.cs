namespace OrderService;

public sealed record OperationResult<T>(bool Succeeded, T? Value, string? Error)
{
    public static OperationResult<T> Success(T value) => new(true, value, null);
    public static OperationResult<T> Failure(string error) => new(false, default, error);
}

public abstract class AsyncOperation<TRequest, TResponse>
{
    public abstract Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken);
}

public delegate void OrderStatusChangedHandler(OrderResponse order);

public interface IOrderProcessor
{
    Task<OperationResult<OrderResponse>> ProcessOrderAsync(Guid orderId, CancellationToken cancellationToken);
}

public sealed class OrderProcessor(OrderStore orderStore, ILogger<OrderProcessor> logger) : AsyncOperation<Guid, OperationResult<OrderResponse>>, IOrderProcessor
{
    public event OrderStatusChangedHandler? StatusChanged;

    public Task<OperationResult<OrderResponse>> ProcessOrderAsync(Guid orderId, CancellationToken cancellationToken)
        => ExecuteAsync(orderId, cancellationToken);

    public override async Task<OperationResult<OrderResponse>> ExecuteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        orderId.ThrowIfEmpty();
        var order = await orderStore.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            return OperationResult<OrderResponse>.Failure("ORDER_NOT_FOUND");
        }

        var nextStatus = order.OrderStatus.GetNextStatus();
        if (nextStatus is null)
        {
            return OperationResult<OrderResponse>.Failure("ORDER_ALREADY_DELIVERED");
        }

        var update = await orderStore.UpdateStatusAsync(orderId, nextStatus.Value, cancellationToken);
        if (update.Order is null || update.InvalidTransition)
        {
            return OperationResult<OrderResponse>.Failure("INVALID_ORDER_TRANSITION");
        }

        StatusChanged?.Invoke(update.Order);
        logger.LogInformation("Order {OrderId} advanced to {Status}.", orderId, update.Order.OrderStatus);
        return OperationResult<OrderResponse>.Success(update.Order);
    }
}

public static class OrderProcessingExtensions
{
    public static void ThrowIfEmpty(this Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("The identifier cannot be empty.", nameof(value));
        }
    }

    public static OrderStatus? GetNextStatus(this OrderStatus status)
        => status switch
        {
            OrderStatus.Created => OrderStatus.Confirmed,
            OrderStatus.Confirmed => OrderStatus.Packed,
            OrderStatus.Packed => OrderStatus.ReadyForDispatch,
            OrderStatus.ReadyForDispatch => OrderStatus.Dispatched,
            OrderStatus.Dispatched => OrderStatus.OutForDelivery,
            OrderStatus.OutForDelivery => OrderStatus.Delivered,
            OrderStatus.Delivered => null,
            _ => null
        };
}