using System.ComponentModel.DataAnnotations;

namespace CustomerService;

public enum CustomerStatus
{
    Active,
    Inactive
}

public sealed record Customer(Guid Id, string Name, string Email, CustomerStatus Status, DateTimeOffset CreatedAt);

public sealed record CreateCustomerRequest(string? Name, string? Email);

public sealed record UpdateCustomerRequest(string? Name, string? Email);

public sealed record CustomerQuery(int Page, int PageSize, CustomerStatus? Status, string? SortBy);

public sealed record CustomerPage(IReadOnlyCollection<Customer> Items, int Page, int PageSize, int TotalCount);

public sealed record ValidationError(string Field, string Message);

public static class CustomerValidation
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static ValidationError? Validate(string? name, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ValidationError("name", "Name is required.");
        }

        if (name.Trim().Length > 120)
        {
            return new ValidationError("name", "Name cannot exceed 120 characters.");
        }

        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 320 || !new EmailAddressAttribute().IsValid(email.Trim()))
        {
            return new ValidationError("email", "A valid email address is required.");
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string[]> ValidateQuery(int? page, int? pageSize, string? sortBy)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (page is <= 0)
        {
            errors["page"] = ["Page must be greater than zero."];
        }

        if (pageSize is <= 0 or > MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {MaxPageSize}."];
        }

        if (!string.IsNullOrWhiteSpace(sortBy) && !IsSupportedSort(sortBy))
        {
            errors["sortBy"] = ["SortBy must be one of: name, createdAt."];
        }

        return errors;
    }

    public static bool IsSupportedSort(string sortBy)
        => sortBy.Equals("name", StringComparison.OrdinalIgnoreCase)
            || sortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase);
}