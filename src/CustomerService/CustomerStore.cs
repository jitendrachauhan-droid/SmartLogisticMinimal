using System.Collections.Concurrent;

namespace CustomerService;

public interface ICustomerStore
{
    Task AddAsync(Customer customer, CancellationToken cancellationToken);
    Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken);
    Task<Customer?> UpdateAsync(Guid id, string name, string email, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class InMemoryCustomerStore : ICustomerStore
{
    private readonly ConcurrentDictionary<Guid, Customer> customers = new();

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        customers[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public Task<Customer?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        customers.TryGetValue(id, out var customer);
        return Task.FromResult(customer);
    }

    public Task<CustomerPage> SearchAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<Customer> filtered = customers.Values;

        if (query.Status is not null)
        {
            filtered = filtered.Where(customer => customer.Status == query.Status);
        }

        filtered = query.SortBy?.ToLowerInvariant() switch
        {
            "name" => filtered.OrderBy(customer => customer.Name),
            "createdat" => filtered.OrderBy(customer => customer.CreatedAt),
            _ => filtered.OrderByDescending(customer => customer.CreatedAt)
        };

        var totalCount = filtered.Count();
        var items = filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return Task.FromResult(new CustomerPage(items, query.Page, query.PageSize, totalCount));
    }

    public Task<Customer?> UpdateAsync(Guid id, string name, string email, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!customers.TryGetValue(id, out var existing))
        {
            return Task.FromResult<Customer?>(null);
        }

        var updated = existing with { Name = name, Email = email };
        customers[id] = updated;
        return Task.FromResult<Customer?>(updated);
    }

    public Task<bool> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!customers.TryGetValue(id, out var existing))
        {
            return Task.FromResult(false);
        }

        customers[id] = existing with { Status = CustomerStatus.Inactive };
        return Task.FromResult(true);
    }
}