using InventoryService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InventoryService.Tests;

public class UnitTest1
{
    [Fact]
    public async Task Two_simultaneous_reservations_cannot_oversell_stock()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new InventoryDbContext(options);
        var store = new InventoryStore(dbContext, CreateCache(), NullLogger<InventoryStore>.Instance);
        var product = await store.AddProductAsync(new CreateProductRequest
        {
            Name = "Scanner",
            Sku = "SCAN-001",
            InitialQuantity = 10,
            LowStockThreshold = 2
        }, CancellationToken.None);

        var results = await Task.WhenAll(
            store.ReserveAsync(product.ProductId, 8, CancellationToken.None),
            store.ReserveAsync(product.ProductId, 8, CancellationToken.None));

        Assert.Equal(1, results.Count(result => result is not null));
        var availability = await store.GetAsync(product.ProductId, CancellationToken.None);
        Assert.NotNull(availability);
        Assert.Equal(2, availability.AvailableQuantity);
        Assert.Equal(8, availability.ReservedQuantity);
        Assert.True(availability.IsLowStock);
    }

    [Fact]
    public async Task Release_and_deduct_update_reserved_stock()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new InventoryDbContext(options);
        var store = new InventoryStore(dbContext, CreateCache(), NullLogger<InventoryStore>.Instance);
        var product = await store.AddProductAsync(new CreateProductRequest
        {
            Name = "Scanner",
            Sku = "SCAN-002",
            InitialQuantity = 10,
            LowStockThreshold = 1
        }, CancellationToken.None);

        await store.ReserveAsync(product.ProductId, 4, CancellationToken.None);
        var released = await store.ReleaseAsync(product.ProductId, 2, CancellationToken.None);
        var deducted = await store.DeductAsync(product.ProductId, 2, CancellationToken.None);

        Assert.Equal(8, released!.AvailableQuantity);
        Assert.Equal(2, released.ReservedQuantity);
        Assert.Equal(8, deducted!.AvailableQuantity);
        Assert.Equal(0, deducted.ReservedQuantity);
    }

    private static IDistributedCache CreateCache()
        => new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider().GetRequiredService<IDistributedCache>();
}