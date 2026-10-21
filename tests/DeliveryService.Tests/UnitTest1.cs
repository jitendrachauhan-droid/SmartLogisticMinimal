using DeliveryService;
using Microsoft.EntityFrameworkCore;

namespace DeliveryService.Tests;

public class UnitTest1
{
    [Fact]
    public void Delivery_state_machine_rejects_skipped_and_reverse_transitions()
    {
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryStatus.Created, DeliveryStatus.Assigned));
        Assert.True(DeliveryStateMachine.CanTransition(DeliveryStatus.OutForDelivery, DeliveryStatus.Delivered));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryStatus.Created, DeliveryStatus.Delivered));
        Assert.False(DeliveryStateMachine.CanTransition(DeliveryStatus.Delivered, DeliveryStatus.Created));
    }

    [Fact]
    public async Task Delivery_lifecycle_creates_tracking_history_and_rejects_invalid_status()
    {
        var options = new DbContextOptionsBuilder<DeliveryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new DeliveryDbContext(options);
        var store = new DeliveryStore(dbContext);
        var agentId = Guid.NewGuid();

        var delivery = await store.CreateAsync(new CreateDeliveryRequest
        {
            OrderId = Guid.NewGuid(),
            DeliveryAddress = "100 Main Street"
        }, CancellationToken.None);

        Assert.Equal(DeliveryStatus.Created, delivery.Status);
        Assert.NotNull(await store.AssignAsync(delivery.Id, agentId, CancellationToken.None));
        Assert.Equal(DeliveryStatus.PickedUp, (await store.UpdateStatusAsync(delivery.Id, DeliveryStatus.PickedUp, CancellationToken.None)).Delivery!.Status);
        Assert.Equal(DeliveryStatus.OutForDelivery, (await store.UpdateStatusAsync(delivery.Id, DeliveryStatus.OutForDelivery, CancellationToken.None)).Delivery!.Status);
        Assert.Equal(DeliveryStatus.Delivered, (await store.UpdateStatusAsync(delivery.Id, DeliveryStatus.Delivered, CancellationToken.None)).Delivery!.Status);

        var invalid = await store.UpdateStatusAsync(delivery.Id, DeliveryStatus.Created, CancellationToken.None);
        var tracking = await store.GetTrackingAsync(delivery.Id, CancellationToken.None);

        Assert.True(invalid.InvalidTransition);
        Assert.NotNull(tracking);
        Assert.Equal(5, tracking.Events.Count);
        Assert.Equal(DeliveryStatus.Delivered, tracking.Delivery.Status);
    }
}