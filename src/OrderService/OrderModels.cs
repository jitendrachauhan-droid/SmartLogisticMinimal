using System.ComponentModel.DataAnnotations;

namespace OrderService;

public enum OrderStatus
{
    Created,
    Confirmed,
    Packed,
    ReadyForDispatch,
    Dispatched,
    OutForDelivery,
    Delivered
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public sealed record CreateOrderRequest
{
    [Required]
    public Guid CustomerId { get; init; }

    [Required]
    public Guid WarehouseId { get; init; }

    [Range(0.01, double.MaxValue)]
    public decimal TotalAmount { get; init; }

    [Required, MaxLength(500)]
    public required string DeliveryAddress { get; init; }

    [Required, MinLength(1)]
    public List<OrderItem> Items { get; init; } = [];


}

public sealed record UpdateOrderStatusRequest
{
    [Required]
    public OrderStatus Status { get; init; }
}

public sealed record UpdatePaymentStatusRequest
{
    [Required]
    public PaymentStatus Status { get; init; }
}

public sealed record OrderResponse(
    Guid OrderId,
    Guid CustomerId,
    Guid WarehouseId,
    DateTimeOffset OrderDate,
    decimal TotalAmount,
    string DeliveryAddress,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus);

public static class OrderStateMachine
{
    public static bool CanTransition(OrderStatus current, OrderStatus next)
        => (current, next) switch
        {
            (OrderStatus.Created, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Packed) => true,
            (OrderStatus.Packed, OrderStatus.ReadyForDispatch) => true,
            (OrderStatus.ReadyForDispatch, OrderStatus.Dispatched) => true,
            (OrderStatus.Dispatched, OrderStatus.OutForDelivery) => true,
            (OrderStatus.OutForDelivery, OrderStatus.Delivered) => true,
            _ => false
        };
}

public static class OrderValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.CustomerId == Guid.Empty)
        {
            errors["customerId"] = ["CustomerId is required."];
        }

        if (request.WarehouseId == Guid.Empty)
        {
            errors["warehouseId"] = ["WarehouseId is required."];
        }

        if (request.TotalAmount <= 0)
        {
            errors["totalAmount"] = ["TotalAmount must be greater than zero."];
        }

        if (string.IsNullOrWhiteSpace(request.DeliveryAddress) || request.DeliveryAddress.Trim().Length > 500)
        {
            errors["deliveryAddress"] = ["DeliveryAddress is required and cannot exceed 500 characters."];
        }

        return errors;
    }
}

public sealed record OrderItem(Guid ProductId, int Quantity, decimal UnitPrice);