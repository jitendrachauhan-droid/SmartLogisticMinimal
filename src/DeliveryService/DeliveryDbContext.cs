using Microsoft.EntityFrameworkCore;

namespace DeliveryService;

public sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();
    public DbSet<DeliveryStatusEventEntity> StatusEvents => Set<DeliveryStatusEventEntity>();
    public DbSet<DeliveryAgentEntity> DeliveryAgents => Set<DeliveryAgentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeliveryEntity>(entity =>
        {
            entity.ToTable("Deliveries");
            entity.HasKey(delivery => delivery.Id);
            entity.HasIndex(delivery => delivery.OrderId).IsUnique();
            entity.HasIndex(delivery => new { delivery.AgentId, delivery.Status });
            entity.Property(delivery => delivery.DeliveryAddress).HasMaxLength(500).IsRequired();
            entity.Property(delivery => delivery.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(delivery => delivery.CreatedAt).IsRequired();
            entity.Property(delivery => delivery.ConcurrencyStamp).IsConcurrencyToken().IsRequired();
            entity.HasOne<DeliveryAgentEntity>()
                .WithMany()
                .HasForeignKey(delivery => delivery.AgentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeliveryAgentEntity>(entity =>
        {
            entity.ToTable("DeliveryAgents");
            entity.HasKey(agent => agent.AgentId);
            entity.HasIndex(agent => agent.Email).IsUnique();
            entity.HasIndex(agent => new { agent.IsAvailable, agent.CurrentLatitude, agent.CurrentLongitude });
            entity.Property(agent => agent.Name).HasMaxLength(120).IsRequired();
            entity.Property(agent => agent.Email).HasMaxLength(320).IsRequired();
            entity.Property(agent => agent.PhoneNumber).HasMaxLength(30);
        });

        modelBuilder.Entity<DeliveryStatusEventEntity>(entity =>
        {
            entity.ToTable("DeliveryStatusEvents");
            entity.HasKey(statusEvent => statusEvent.Id);
            entity.HasIndex(statusEvent => new { statusEvent.DeliveryId, statusEvent.OccurredAt });
            entity.Property(statusEvent => statusEvent.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasOne<DeliveryEntity>()
                .WithMany()
                .HasForeignKey(statusEvent => statusEvent.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class DeliveryEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public required string DeliveryAddress { get; set; }
    public Guid? AgentId { get; set; }
    public DeliveryStatus Status { get; set; }
    public DateTimeOffset? EstimatedDeliveryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public Guid ConcurrencyStamp { get; set; }
}

public sealed class DeliveryStatusEventEntity
{
    public Guid Id { get; set; }
    public Guid DeliveryId { get; set; }
    public DeliveryStatus Status { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class DeliveryStore(DeliveryDbContext dbContext)
{
    public async Task<DeliveryResponse> CreateAsync(CreateDeliveryRequest request, CancellationToken cancellationToken)
    {
        var delivery = new DeliveryEntity
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            DeliveryAddress = request.DeliveryAddress.Trim(),
            Status = DeliveryStatus.Created,
            EstimatedDeliveryAt = request.EstimatedDeliveryAt,
            CreatedAt = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid()
        };
        dbContext.Deliveries.Add(delivery);
        AddStatusEvent(delivery.Id, DeliveryStatus.Created);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(delivery);
    }

    public async Task<DeliveryResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
        => (await dbContext.Deliveries.AsNoTracking().SingleOrDefaultAsync(delivery => delivery.Id == id, cancellationToken)) is { } delivery
            ? ToResponse(delivery)
            : null;

    public async Task<DeliveryResponse?> AssignAsync(Guid id, Guid agentId, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (delivery is null || delivery.Status != DeliveryStatus.Created)
        {
            return null;
        }

        delivery.AgentId = agentId;
        delivery.Status = DeliveryStatus.Assigned;
        delivery.ConcurrencyStamp = Guid.NewGuid();
        AddStatusEvent(delivery.Id, delivery.Status);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(delivery);
    }

    public async Task<(DeliveryResponse? Delivery, bool InvalidTransition)> UpdateStatusAsync(Guid id, DeliveryStatus nextStatus, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (delivery is null)
        {
            return (null, false);
        }

        if (!DeliveryStateMachine.CanTransition(delivery.Status, nextStatus))
        {
            return (ToResponse(delivery), true);
        }

        delivery.Status = nextStatus;
        delivery.DeliveredAt = nextStatus == DeliveryStatus.Delivered ? DateTimeOffset.UtcNow : delivery.DeliveredAt;
        delivery.ConcurrencyStamp = Guid.NewGuid();
        AddStatusEvent(delivery.Id, nextStatus);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (ToResponse(delivery), false);
    }

    public async Task<DeliveryTrackingResponse?> GetTrackingAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        var events = await dbContext.StatusEvents.AsNoTracking()
            .Where(statusEvent => statusEvent.DeliveryId == id)
            .OrderBy(statusEvent => statusEvent.OccurredAt)
            .Select(statusEvent => new DeliveryStatusEvent(statusEvent.Status, statusEvent.OccurredAt))
            .ToArrayAsync(cancellationToken);
        return new DeliveryTrackingResponse(ToResponse(delivery), events);
    }

    private void AddStatusEvent(Guid deliveryId, DeliveryStatus status)
        => dbContext.StatusEvents.Add(new DeliveryStatusEventEntity
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            Status = status,
            OccurredAt = DateTimeOffset.UtcNow
        });

    private static DeliveryResponse ToResponse(DeliveryEntity delivery)
        => new(delivery.Id, delivery.OrderId, delivery.DeliveryAddress, delivery.AgentId, delivery.Status, delivery.EstimatedDeliveryAt, delivery.CreatedAt, delivery.DeliveredAt);
}

public sealed class DeliveryAgentEntity
{
    public Guid AgentId { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsAvailable { get; set; }
    public double CurrentLatitude { get; set; }
    public double CurrentLongitude { get; set; }
}