namespace CustomerService.Tests;

public class UnitTest1
{
    [Theory]
    [InlineData("page", "Page must be greater than zero.")]
    [InlineData("pageSize", "PageSize must be between 1 and 100.")]
    [InlineData("sortBy", "SortBy must be one of: name, createdAt.")]
    public void Search_query_validation_rejects_invalid_values(string field, string message)
    {
        var errors = field switch
        {
            "page" => CustomerValidation.ValidateQuery(0, 20, null),
            "pageSize" => CustomerValidation.ValidateQuery(1, 101, null),
            _ => CustomerValidation.ValidateQuery(1, 20, "email")
        };

        Assert.Contains(field, errors.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(message, errors[field].Single());
    }

    [Theory]
    [InlineData("Alice", "alice@example.com", true)]
    [InlineData("", "alice@example.com", false)]
    [InlineData("Alice", "alice@", false)]
    [InlineData("Alice", "", false)]
    public void Customer_validation_checks_required_and_email_values(string name, string email, bool isValid)
    {
        Assert.Equal(isValid, CustomerValidation.Validate(name, email) is null);
    }

    [Fact]
    public async Task Search_filters_and_paginates_customers()
    {
        var store = new InMemoryCustomerStore();
        await store.AddAsync(new Customer(Guid.NewGuid(), "Alice Smith", "alice@example.com", CustomerStatus.Active, DateTimeOffset.UtcNow.AddMinutes(-2)), CancellationToken.None);
        await store.AddAsync(new Customer(Guid.NewGuid(), "Bob Jones", "bob@example.com", CustomerStatus.Inactive, DateTimeOffset.UtcNow.AddMinutes(-1)), CancellationToken.None);

        var result = await store.SearchAsync(new CustomerQuery(1, 10, CustomerStatus.Active, "name"), CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("Alice Smith", result.Items.Single().Name);
    }
}
