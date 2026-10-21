using System.Text.Json;

namespace Shared.Contracts;

public static class EventNames
{
    public const string OrderCreated = "OrderCreated";
    public const string StockReserved = "StockReserved";
    public const string OrderPacked = "OrderPacked";
    public const string OrderDispatched = "OrderDispatched";
    public const string DeliveryAssigned = "DeliveryAssigned";
    public const string OrderDelivered = "OrderDelivered";
}

public sealed record IntegrationEvent(string EventId, string EventName, DateTimeOffset OccurredAt, string Payload);

public sealed record OrderCreatedEvent(
    Guid OrderId,
    Guid CustomerId,
    Guid WarehouseId,
    decimal TotalAmount,
    string DeliveryAddress,
    DateTimeOffset OccurredAt)
{
    public IntegrationEvent ToIntegrationEvent()
        => new(Guid.NewGuid().ToString(), EventNames.OrderCreated, OccurredAt, JsonSerializer.Serialize(this));
}

public sealed record StockReservedEvent(Guid OrderId, Guid ProductId, int Quantity, DateTimeOffset OccurredAt);
public sealed record OrderPackedEvent(Guid OrderId, DateTimeOffset OccurredAt);
public sealed record OrderDispatchedEvent(Guid OrderId, DateTimeOffset OccurredAt);
public sealed record DeliveryAssignedEvent(Guid OrderId, Guid DeliveryId, Guid AgentId, DateTimeOffset OccurredAt);
public sealed record OrderDeliveredEvent(Guid OrderId, Guid DeliveryId, DateTimeOffset OccurredAt);
