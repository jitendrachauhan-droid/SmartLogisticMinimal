using System.ComponentModel.DataAnnotations;

namespace InventoryService;

public sealed record CreateProductRequest
{
    [Required, MaxLength(120)]
    public required string Name { get; init; }

    [Required, MaxLength(80)]
    public required string Sku { get; init; }

    [Range(0, int.MaxValue)]
    public int InitialQuantity { get; init; }

    [Range(0, int.MaxValue)]
    public int LowStockThreshold { get; init; }
}

public sealed record UpdateProductRequest
{
    [Required, MaxLength(120)]
    public required string Name { get; init; }

    [Range(0, int.MaxValue)]
    public int LowStockThreshold { get; init; }
}

public sealed record StockOperationRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed record ProductResponse(Guid ProductId, string Name, string Sku, int AvailableQuantity, int ReservedQuantity, int LowStockThreshold, bool IsLowStock);

public static class InventoryValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(CreateProductRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            errors["sku"] = ["Sku is required."];
        }

        if (request.InitialQuantity < 0)
        {
            errors["initialQuantity"] = ["InitialQuantity cannot be negative."];
        }

        if (request.LowStockThreshold < 0)
        {
            errors["lowStockThreshold"] = ["LowStockThreshold cannot be negative."];
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> Validate(StockOperationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ProductId == Guid.Empty)
        {
            errors["productId"] = ["ProductId is required."];
        }

        if (request.Quantity <= 0)
        {
            errors["quantity"] = ["Quantity must be greater than zero."];
        }

        return errors;
    }
}