using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WarehouseService;

namespace WarehouseService.Tests;

public class UnitTest1
{
    [Fact]
    public async Task Warehouse_selection_requires_stock_and_capacity_then_prefers_nearest_location()
    {
        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new WarehouseDbContext(options);
        var store = new WarehouseStore(dbContext, CreateCache(), NullLogger<WarehouseStore>.Instance);
        var productId = Guid.NewGuid();

        var farWarehouse = await store.CreateAsync(new CreateWarehouseRequest
        {
            Name = "Far Warehouse", Address = "Far", Latitude = 41, Longitude = -75, Capacity = 100
        }, CancellationToken.None);
        var nearWarehouse = await store.CreateAsync(new CreateWarehouseRequest
        {
            Name = "Near Warehouse", Address = "Near", Latitude = 40.1, Longitude = -74.1, Capacity = 100
        }, CancellationToken.None);
        await store.MapInventoryAsync(farWarehouse.WarehouseId, new MapInventoryRequest { ProductId = productId, Quantity = 10 }, CancellationToken.None);
        await store.MapInventoryAsync(nearWarehouse.WarehouseId, new MapInventoryRequest { ProductId = productId, Quantity = 10 }, CancellationToken.None);

        var selected = await store.SelectAsync(new SelectWarehouseRequest
        {
            CustomerLatitude = 40,
            CustomerLongitude = -74,
            Items = [new FulfilmentItem(productId, 2)]
        }, CancellationToken.None);

        Assert.NotNull(selected);
        Assert.Equal(nearWarehouse.WarehouseId, selected.Warehouse.WarehouseId);
    }

    [Fact]
    public async Task Inventory_mapping_cannot_exceed_warehouse_capacity()
    {
        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new WarehouseDbContext(options);
        var store = new WarehouseStore(dbContext, CreateCache(), NullLogger<WarehouseStore>.Instance);
        var warehouse = await store.CreateAsync(new CreateWarehouseRequest
        {
            Name = "Small Warehouse", Address = "Small", Capacity = 5
        }, CancellationToken.None);

        var result = await store.MapInventoryAsync(warehouse.WarehouseId, new MapInventoryRequest
        {
            ProductId = Guid.NewGuid(), Quantity = 6
        }, CancellationToken.None);

        Assert.Null(result);
    }

    private static IDistributedCache CreateCache()
        => new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider().GetRequiredService<IDistributedCache>();
}