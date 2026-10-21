using Microsoft.EntityFrameworkCore;

namespace CustomerService;

public sealed class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerEntity>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Name).HasMaxLength(120).IsRequired();
            entity.Property(customer => customer.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(customer => customer.Email).IsUnique();
            entity.Property(customer => customer.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(customer => customer.CreatedAt).IsRequired();
        });
    }
}

public sealed class CustomerEntity
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public CustomerStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Customer ToModel() => new(Id, Name, Email, Status, CreatedAt);

    public static CustomerEntity FromModel(Customer customer) => new()
    {
        Id = customer.Id,
        Name = customer.Name,
        Email = customer.Email,
        Status = customer.Status,
        CreatedAt = customer.CreatedAt
    };
}

public sealed class EfCustomerStore(CustomerDbContext dbContext) : ICustomerStore
{
    public async Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        await dbContext.Customers.AddAsync(CustomerEntity.FromModel(customer), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken)
        => (await dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken))?.ToModel();

    public async Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        IQueryable<CustomerEntity> customers = dbContext.Customers.AsNoTracking();
        if (query.Status is not null)
        {
            customers = customers.Where(customer => customer.Status == query.Status);
        }

        customers = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => customers.OrderBy(customer => customer.Name),
            "createdat" => customers.OrderBy(customer => customer.CreatedAt),
            _ => customers.OrderByDescending(customer => customer.CreatedAt)
        };

        var totalCount = await customers.CountAsync(cancellationToken);
        var items = await customers
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(customer => new Customer(customer.Id, customer.Name, customer.Email, customer.Status, customer.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return new CustomerPage(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Customer?> UpdateAsync(Guid id, string name, string email, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        customer.Name = name;
        customer.Email = email;
        await dbContext.SaveChangesAsync(cancellationToken);
        return customer.ToModel();
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        customer.Status = CustomerStatus.Inactive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}