using System.ComponentModel.DataAnnotations;

namespace DeliveryService;

public enum DeliveryStatus
{
    Created,
    Assigned,
    PickedUp,
    OutForDelivery,
    Delivered
}

public sealed record CreateDeliveryRequest
{
    [Required]
    public Guid OrderId { get; init; }

    [Required, MaxLength(500)]
    public required string DeliveryAddress { get; init; }

    public DateTimeOffset? EstimatedDeliveryAt { get; init; }
}

public sealed record AssignDeliveryAgentRequest
{
    [Required]
    public Guid AgentId { get; init; }
}

public sealed record UpdateDeliveryStatusRequest
{
    [Required]
    public DeliveryStatus Status { get; init; }
}

public sealed record DeliveryResponse(
    Guid Id,
    Guid OrderId,
    string DeliveryAddress,
    Guid? AgentId,
    DeliveryStatus Status,
    DateTimeOffset? EstimatedDeliveryAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt);

public sealed record DeliveryTrackingResponse(
    DeliveryResponse Delivery,
    IReadOnlyCollection<DeliveryStatusEvent> Events);

public sealed record DeliveryStatusEvent(DeliveryStatus Status, DateTimeOffset OccurredAt);

public static class DeliveryStateMachine
{
    public static bool CanTransition(DeliveryStatus current, DeliveryStatus next)
        => (current, next) switch
        {
            (DeliveryStatus.Created, DeliveryStatus.Assigned) => true,
            (DeliveryStatus.Assigned, DeliveryStatus.PickedUp) => true,
            (DeliveryStatus.PickedUp, DeliveryStatus.OutForDelivery) => true,
            (DeliveryStatus.OutForDelivery, DeliveryStatus.Delivered) => true,
            _ => false
        };
}