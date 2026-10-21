using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace InventoryService;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<InventoryEntity> Inventory => Set<InventoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(product => product.ProductId);
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Name).HasMaxLength(120).IsRequired();
            entity.Property(product => product.Sku).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<InventoryEntity>(entity =>
        {
            entity.ToTable("Inventory");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ConcurrencyStamp).IsRowVersion();
            entity.HasOne<ProductEntity>()
                .WithOne()
                .HasForeignKey<InventoryEntity>(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(item => new { item.AvailableQuantity, item.LowStockThreshold });
        });
    }
}

public sealed class ProductEntity
{
    public Guid ProductId { get; set; }
    public required string Name { get; set; }
    public required string Sku { get; set; }
}

public sealed class InventoryEntity
{
    public Guid ProductId { get; set; }
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int LowStockThreshold { get; set; }
    public byte[] ConcurrencyStamp { get; set; } = [];
}

public sealed class InventoryStore(InventoryDbContext dbContext, IDistributedCache cache, ILogger<InventoryStore> logger)
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(5),
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };
    private readonly SemaphoreSlim mutationLock = new(1, 1);

    public async Task<ProductResponse> AddProductAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = new ProductEntity
        {
            ProductId = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Sku = request.Sku.Trim()
        };
        var inventory = new InventoryEntity
        {
            ProductId = product.ProductId,
            AvailableQuantity = request.InitialQuantity,
            LowStockThreshold = request.LowStockThreshold
        };
        dbContext.Products.Add(product);
        dbContext.Inventory.Add(inventory);
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(product, inventory);
        await RemoveCacheAsync(response.ProductId, cancellationToken);
        return response;
    }

    public async Task<ProductResponse?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(productId);
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                logger.LogDebug("Inventory cache hit for product {ProductId}.", productId);
                return JsonSerializer.Deserialize<ProductResponse>(cached);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Inventory cache read failed for product {ProductId}; using SQL Server.", productId);
        }

        var response = await QueryProduct(productId).SingleOrDefaultAsync(cancellationToken);
        if (response is not null)
        {
            try
            {
                await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(response), CacheOptions, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Inventory cache write failed for product {ProductId}.", productId);
            }
        }

        return response;
    }

    public async Task<ProductResponse?> UpdateProductAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var inventory = await dbContext.Inventory.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        if (product is null || inventory is null)
        {
            return null;
        }

        product.Name = request.Name.Trim();
        inventory.LowStockThreshold = request.LowStockThreshold;
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = ToResponse(product, inventory);
        await RemoveCacheAsync(response.ProductId, cancellationToken);
        return response;
    }

    public async Task<ProductResponse?> ReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken)
        => await ChangeStockAsync(productId, quantity, reserve: true, cancellationToken);

    public async Task<ProductResponse?> ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken)
        => await ChangeStockAsync(productId, quantity, reserve: false, cancellationToken);

    public async Task<ProductResponse?> DeductAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        await mutationLock.WaitAsync(cancellationToken);
        try
        {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var inventory = await dbContext.Inventory.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var product = await dbContext.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        if (inventory is null || product is null || inventory.ReservedQuantity < quantity)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        inventory.ReservedQuantity -= quantity;
        await SaveAndCommitAsync(transaction, cancellationToken);
        var response = ToResponse(product, inventory);
        await RemoveCacheAsync(response.ProductId, cancellationToken);
        return response;
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<ProductResponse?> ChangeStockAsync(Guid productId, int quantity, bool reserve, CancellationToken cancellationToken)
    {
        await mutationLock.WaitAsync(cancellationToken);
        try
        {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var inventory = await dbContext.Inventory.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        var product = await dbContext.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);
        if (inventory is null || product is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (reserve)
        {
            if (inventory.AvailableQuantity < quantity)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            inventory.AvailableQuantity -= quantity;
            inventory.ReservedQuantity += quantity;
        }
        else
        {
            if (inventory.ReservedQuantity < quantity)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            inventory.ReservedQuantity -= quantity;
            inventory.AvailableQuantity += quantity;
        }

        await SaveAndCommitAsync(transaction, cancellationToken);
        var response = ToResponse(product, inventory);
        await RemoveCacheAsync(response.ProductId, cancellationToken);
        return response;
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return new NoOpTransaction();
        }

        return await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
    }

    private async Task SaveAndCommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private IQueryable<ProductResponse> QueryProduct(Guid productId)
        => from product in dbContext.Products.AsNoTracking()
           join inventory in dbContext.Inventory.AsNoTracking() on product.ProductId equals inventory.ProductId
           where product.ProductId == productId
           select new ProductResponse(product.ProductId, product.Name, product.Sku, inventory.AvailableQuantity, inventory.ReservedQuantity, inventory.LowStockThreshold, inventory.AvailableQuantity <= inventory.LowStockThreshold);

    private static ProductResponse ToResponse(ProductEntity product, InventoryEntity inventory)
        => new(product.ProductId, product.Name, product.Sku, inventory.AvailableQuantity, inventory.ReservedQuantity, inventory.LowStockThreshold, inventory.AvailableQuantity <= inventory.LowStockThreshold);

    private static string CacheKey(Guid productId) => $"inventory:product:{productId:N}";

    private async Task RemoveCacheAsync(Guid productId, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(CacheKey(productId), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Inventory cache invalidation failed for product {ProductId}.", productId);
        }
    }

    private sealed class NoOpTransaction : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
    {
        public Guid TransactionId => Guid.Empty;
        public Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction GetDbTransaction() => this;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Commit() { }
        public void Rollback() { }
    }
}