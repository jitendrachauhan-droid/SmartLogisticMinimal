using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService;

namespace OrderService.Tests;

public class UnitTest1
{
    [Fact]
    public async Task ProcessOrderAsync_advances_order_and_supports_status_delegate()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new OrderDbContext(options);
        var store = new OrderStore(dbContext);
        var order = await store.CreateAsync(new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            TotalAmount = 10,
            DeliveryAddress = "100 Main Street"
        }, CancellationToken.None);
        var processor = new OrderProcessor(store, NullLogger<OrderProcessor>.Instance);
        OrderStatus? observedStatus = null;
        processor.StatusChanged += changedOrder => observedStatus = changedOrder.OrderStatus;

        var result = await processor.ProcessOrderAsync(order.OrderId, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(OrderStatus.Confirmed, result.Value!.OrderStatus);
        Assert.Equal(OrderStatus.Confirmed, observedStatus);
    }

    [Fact]
    public async Task ProcessOrderAsync_honors_cancellation()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new OrderDbContext(options);
        var processor = new OrderProcessor(new OrderStore(dbContext), NullLogger<OrderProcessor>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessOrderAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public void Order_state_machine_allows_only_the_declared_forward_lifecycle()
    {
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Created, OrderStatus.Confirmed));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Confirmed, OrderStatus.Packed));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Packed, OrderStatus.ReadyForDispatch));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.ReadyForDispatch, OrderStatus.Dispatched));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.Dispatched, OrderStatus.OutForDelivery));
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.OutForDelivery, OrderStatus.Delivered));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Delivered, OrderStatus.Created));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.Created, OrderStatus.Delivered));
    }

    [Fact]
    public void Order_validation_rejects_missing_ids_invalid_amount_and_address()
    {
        var errors = OrderValidation.Validate(new CreateOrderRequest
        {
            TotalAmount = 0,
            DeliveryAddress = ""
        });

        Assert.Contains("customerId", errors.Keys);
        Assert.Contains("warehouseId", errors.Keys);
        Assert.Contains("totalAmount", errors.Keys);
        Assert.Contains("deliveryAddress", errors.Keys);
    }

    [Fact]
    public async Task Order_store_persists_order_and_rejects_delivered_to_created()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new OrderDbContext(options);
        var store = new OrderStore(dbContext);
        var request = new CreateOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            WarehouseId = Guid.NewGuid(),
            TotalAmount = 125.50m,
            DeliveryAddress = "100 Main Street"
        };

        var order = await store.CreateAsync(request, CancellationToken.None);
        Assert.Equal(OrderStatus.Created, order.OrderStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);

        foreach (var nextStatus in new[]
        {
            OrderStatus.Confirmed,
            OrderStatus.Packed,
            OrderStatus.ReadyForDispatch,
            OrderStatus.Dispatched,
            OrderStatus.OutForDelivery,
            OrderStatus.Delivered
        })
        {
            var result = await store.UpdateStatusAsync(order.OrderId, nextStatus, CancellationToken.None);
            Assert.False(result.InvalidTransition);
            Assert.Equal(nextStatus, result.Order!.OrderStatus);
        }

        var invalid = await store.UpdateStatusAsync(order.OrderId, OrderStatus.Created, CancellationToken.None);
        var updatedPayment = await store.UpdatePaymentStatusAsync(order.OrderId, PaymentStatus.Paid, CancellationToken.None);

        Assert.True(invalid.InvalidTransition);
        Assert.Equal(OrderStatus.Delivered, invalid.Order!.OrderStatus);
        Assert.Equal(PaymentStatus.Paid, updatedPayment!.PaymentStatus);
    }
}