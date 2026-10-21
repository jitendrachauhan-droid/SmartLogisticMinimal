using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace WarehouseService;

public sealed class WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : DbContext(options)
{
    public DbSet<WarehouseEntity> Warehouses => Set<WarehouseEntity>();
    public DbSet<WarehouseInventoryEntity> Inventory => Set<WarehouseInventoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WarehouseEntity>(entity =>
        {
            entity.ToTable("Warehouses");
            entity.HasKey(warehouse => warehouse.WarehouseId);
            entity.HasIndex(warehouse => new { warehouse.IsAvailable, warehouse.Capacity });
            entity.Property(warehouse => warehouse.Name).HasMaxLength(120).IsRequired();
            entity.Property(warehouse => warehouse.Address).HasMaxLength(500).IsRequired();
            entity.Property(warehouse => warehouse.ConcurrencyStamp).IsConcurrencyToken().IsRequired();
        });

        modelBuilder.Entity<WarehouseInventoryEntity>(entity =>
        {
            entity.ToTable("WarehouseInventory");
            entity.HasKey(item => new { item.WarehouseId, item.ProductId });
            entity.HasIndex(item => new { item.ProductId, item.Quantity });
            entity.HasOne<WarehouseEntity>()
                .WithMany()
                .HasForeignKey(item => item.WarehouseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class WarehouseEntity
{
    public Guid WarehouseId { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Capacity { get; set; }
    public int UsedCapacity { get; set; }
    public bool IsAvailable { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

public sealed class WarehouseInventoryEntity
{
    public Guid WarehouseId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class WarehouseStore(WarehouseDbContext dbContext, IDistributedCache cache, ILogger<WarehouseStore> logger)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
    };
    public async Task<WarehouseResponse> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = new WarehouseEntity
        {
            WarehouseId = Guid.NewGuid(), Name = request.Name.Trim(), Address = request.Address.Trim(),
            Latitude = request.Latitude, Longitude = request.Longitude, Capacity = request.Capacity,
            IsAvailable = true, ConcurrencyStamp = Guid.NewGuid()
        };
        dbContext.Warehouses.Add(warehouse);
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(warehouse);
        await RemoveWarehouseCacheAsync(response.WarehouseId, cancellationToken);
        return response;
    }

    public async Task<WarehouseResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var cacheKey = WarehouseCacheKey(id);
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                logger.LogDebug("Warehouse cache hit for {WarehouseId}.", id);
                return JsonSerializer.Deserialize<WarehouseResponse>(cached);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Warehouse cache read failed for {WarehouseId}; using SQL Server.", id);
        }

        var warehouse = await dbContext.Warehouses.AsNoTracking().SingleOrDefaultAsync(item => item.WarehouseId == id, cancellationToken);
        if (warehouse is null)
        {
            return null;
        }

        var response = ToResponse(warehouse);
        try
        {
            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), CacheOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Warehouse cache write failed for {WarehouseId}.", id);
        }

        return response;
    }

    public async Task<WarehouseResponse?> UpdateAsync(Guid id, UpdateWarehouseRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.Warehouses.SingleOrDefaultAsync(item => item.WarehouseId == id, cancellationToken);
        if (warehouse is null || request.Capacity < warehouse.UsedCapacity || request.Capacity <= 0)
        {
            return null;
        }

        warehouse.Name = request.Name.Trim(); warehouse.Address = request.Address.Trim(); warehouse.Latitude = request.Latitude;
        warehouse.Longitude = request.Longitude; warehouse.Capacity = request.Capacity; warehouse.IsAvailable = request.IsAvailable;
        warehouse.ConcurrencyStamp = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(warehouse);
        await RemoveWarehouseCacheAsync(response.WarehouseId, cancellationToken);
        return response;
    }

    public async Task<WarehouseResponse?> MapInventoryAsync(Guid warehouseId, MapInventoryRequest request, CancellationToken cancellationToken)
    {
        var warehouse = await dbContext.Warehouses.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId, cancellationToken);
        if (warehouse is null || request.ProductId == Guid.Empty || request.Quantity < 0 || warehouse.UsedCapacity + request.Quantity > warehouse.Capacity)
        {
            return null;
        }

        var mapping = await dbContext.Inventory.SingleOrDefaultAsync(item => item.WarehouseId == warehouseId && item.ProductId == request.ProductId, cancellationToken);
        if (mapping is null)
        {
            mapping = new WarehouseInventoryEntity { WarehouseId = warehouseId, ProductId = request.ProductId, Quantity = request.Quantity };
            dbContext.Inventory.Add(mapping);
            warehouse.UsedCapacity += request.Quantity;
        }
        else
        {
            warehouse.UsedCapacity += request.Quantity - mapping.Quantity;
            mapping.Quantity = request.Quantity;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(warehouse);
        await RemoveWarehouseCacheAsync(response.WarehouseId, cancellationToken);
        return response;
    }

    public async Task<WarehouseSelectionResponse?> SelectAsync(SelectWarehouseRequest request, CancellationToken cancellationToken)
    {
        var items = request.Items.Where(item => item.ProductId != Guid.Empty && item.Quantity > 0).ToArray();
        if (items.Length != request.Items.Count)
        {
            return null;
        }

        var selectionCacheKey = SelectionCacheKey(request);
        try
        {
            var cached = await cache.GetStringAsync(selectionCacheKey, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<WarehouseSelectionResponse>(cached);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Warehouse selection cache read failed; using SQL Server.");
        }

        var requiredCapacity = items.Sum(item => item.Quantity);
        IQueryable<WarehouseEntity> candidateQuery = dbContext.Warehouses.AsNoTracking()
            .Where(warehouse => warehouse.IsAvailable && warehouse.Capacity - warehouse.UsedCapacity >= requiredCapacity);
        foreach (var requested in items)
        {
            var productId = requested.ProductId;
            var quantity = requested.Quantity;
            candidateQuery = candidateQuery.Where(warehouse => dbContext.Inventory.Any(stock =>
                stock.WarehouseId == warehouse.WarehouseId && stock.ProductId == productId && stock.Quantity >= quantity));
        }

        var candidates = await candidateQuery.ToArrayAsync(cancellationToken);
        var selected = candidates
            .Select(warehouse => new { Warehouse = warehouse, Distance = Distance(warehouse.Latitude, warehouse.Longitude, request.CustomerLatitude, request.CustomerLongitude) })
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        var selection = new WarehouseSelectionResponse(ToResponse(selected.Warehouse), selected.Distance);
        try
        {
            await cache.SetStringAsync(selectionCacheKey, JsonSerializer.Serialize(selection), CacheOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Warehouse selection cache write failed.");
        }

        return selection;
    }

    private static double Distance(double firstLatitude, double firstLongitude, double secondLatitude, double secondLongitude)
    {
        var latitudeDifference = firstLatitude - secondLatitude;
        var longitudeDifference = firstLongitude - secondLongitude;
        return Math.Sqrt(latitudeDifference * latitudeDifference + longitudeDifference * longitudeDifference);
    }

    private static WarehouseResponse ToResponse(WarehouseEntity warehouse)
        => new(warehouse.WarehouseId, warehouse.Name, warehouse.Address, warehouse.Latitude, warehouse.Longitude, warehouse.Capacity, warehouse.UsedCapacity, warehouse.Capacity - warehouse.UsedCapacity, warehouse.IsAvailable);

    private static string WarehouseCacheKey(Guid warehouseId) => $"warehouse:{warehouseId:N}";

    private static string SelectionCacheKey(SelectWarehouseRequest request)
    {
        var items = string.Join(',', request.Items.OrderBy(item => item.ProductId).Select(item => $"{item.ProductId:N}:{item.Quantity}"));
        return $"warehouse:selection:{request.CustomerLatitude:F6}:{request.CustomerLongitude:F6}:{items}";
    }

    private async Task RemoveWarehouseCacheAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(WarehouseCacheKey(warehouseId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Warehouse cache invalidation failed for {WarehouseId}.", warehouseId);
        }
    }
}