using Microsoft.EntityFrameworkCore;

namespace OrderService;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItemEntity> OrderItems => Set<OrderItemEntity>();
    public DbSet<PaymentEntity> Payments => Set<PaymentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(order => order.OrderId);
            entity.HasIndex(order => new { order.CustomerId, order.OrderDate });
            entity.HasIndex(order => new { order.WarehouseId, order.OrderStatus });
            entity.Property(order => order.TotalAmount).HasPrecision(18, 2).IsRequired();
            entity.Property(order => order.DeliveryAddress).HasMaxLength(500).IsRequired();
            entity.Property(order => order.OrderStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(order => order.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(order => order.OrderDate).IsRequired();
            entity.Property(order => order.ConcurrencyStamp).IsConcurrencyToken().IsRequired();
            entity.HasMany(order => order.Items)
                .WithOne(item => item.Order)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(order => order.Payments)
                .WithOne(payment => payment.Order)
                .HasForeignKey(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable(table => table.HasCheckConstraint("CK_Orders_TotalAmount_NonNegative", "[TotalAmount] >= 0"));
        });

        modelBuilder.Entity<OrderItemEntity>(entity =>
        {
            entity.ToTable("OrderItems", table => table.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0"));
            entity.HasKey(item => item.OrderItemId);
            entity.HasIndex(item => new { item.OrderId, item.ProductId }).IsUnique();
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2).IsRequired();
            entity.Property(item => item.Quantity).IsRequired();
            entity.Property(item => item.ProductId).IsRequired();
        });

        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.PaymentId);
            entity.HasIndex(payment => new { payment.OrderId, payment.CreatedAt });
            entity.HasIndex(payment => payment.ProviderReference).IsUnique().HasFilter("[ProviderReference] IS NOT NULL");
            entity.Property(payment => payment.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(payment => payment.ProviderReference).HasMaxLength(200);
            entity.ToTable(table => table.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0"));
        });
    }
}

public sealed class OrderEntity
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid WarehouseId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public required string DeliveryAddress { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public ICollection<OrderItemEntity> Items { get; set; } = [];
    public ICollection<PaymentEntity> Payments { get; set; } = [];

    public OrderResponse ToResponse() => new(OrderId, CustomerId, WarehouseId, OrderDate, TotalAmount, DeliveryAddress, OrderStatus, PaymentStatus);
}

public sealed class OrderStore(OrderDbContext dbContext)
{
    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = new OrderEntity
        {
            OrderId = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            WarehouseId = request.WarehouseId,
            OrderDate = DateTimeOffset.UtcNow,
            TotalAmount = request.TotalAmount,
            DeliveryAddress = request.DeliveryAddress.Trim(),
            OrderStatus = OrderStatus.Created,
            PaymentStatus = PaymentStatus.Pending,
            ConcurrencyStamp = Guid.NewGuid(),
            Items = request.Items.Select(item => new OrderItemEntity
            {
                OrderItemId = Guid.NewGuid(),
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList()
        };
        // Create a payment record explicitly
        var payment = new PaymentEntity
        {
            PaymentId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Amount = order.TotalAmount,
            Status = PaymentStatus.Pending,
            ProviderReference = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        order.Payments.Add(payment);

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        return order.ToResponse();
    }

    public async Task<OrderResponse?> GetAsync(Guid orderId, CancellationToken cancellationToken)
        => (await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.OrderId == orderId, cancellationToken))?.ToResponse();

    public async Task<(OrderResponse? Order, bool InvalidTransition)> UpdateStatusAsync(Guid orderId, OrderStatus nextStatus, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is null)
        {
            return (null, false);
        }

        if (!OrderStateMachine.CanTransition(order.OrderStatus, nextStatus))
        {
            return (order.ToResponse(), true);
        }

        order.OrderStatus = nextStatus;
        order.ConcurrencyStamp = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        return (order.ToResponse(), false);
    }

    public async Task<OrderResponse?> UpdatePaymentStatusAsync(Guid orderId, PaymentStatus status, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.SingleOrDefaultAsync(item => item.OrderId == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        order.PaymentStatus = status;
        order.ConcurrencyStamp = Guid.NewGuid();
        await dbContext.SaveChangesAsync(cancellationToken);
        return order.ToResponse();
    }
}

public sealed class OrderItemEntity
{
    public Guid OrderItemId { get; set; }
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public OrderEntity? Order { get; set; }
}

public sealed class PaymentEntity
{
    public Guid PaymentId { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public OrderEntity? Order { get; set; }
}