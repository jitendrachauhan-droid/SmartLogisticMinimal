using System.ComponentModel.DataAnnotations;

namespace WarehouseService;

public sealed record CreateWarehouseRequest
{
    [Required, MaxLength(120)]
    public required string Name { get; init; }

    [Required, MaxLength(500)]
    public required string Address { get; init; }

    [Range(-90, 90)]
    public double Latitude { get; init; }

    [Range(-180, 180)]
    public double Longitude { get; init; }

    [Range(1, int.MaxValue)]
    public int Capacity { get; init; }
}

public sealed record UpdateWarehouseRequest
{
    [Required, MaxLength(120)]
    public required string Name { get; init; }

    [Required, MaxLength(500)]
    public required string Address { get; init; }

    [Range(-90, 90)]
    public double Latitude { get; init; }

    [Range(-180, 180)]
    public double Longitude { get; init; }

    [Range(1, int.MaxValue)]
    public int Capacity { get; init; }

    public bool IsAvailable { get; init; }
}

public sealed record MapInventoryRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(0, int.MaxValue)]
    public int Quantity { get; init; }
}

public sealed record FulfilmentItem(Guid ProductId, int Quantity);

public sealed record SelectWarehouseRequest
{
    [Range(-90, 90)]
    public double CustomerLatitude { get; init; }

    [Range(-180, 180)]
    public double CustomerLongitude { get; init; }

    [MinLength(1)]
    public required IReadOnlyCollection<FulfilmentItem> Items { get; init; }
}

public sealed record WarehouseResponse(Guid WarehouseId, string Name, string Address, double Latitude, double Longitude, int Capacity, int UsedCapacity, int AvailableCapacity, bool IsAvailable);

public sealed record WarehouseSelectionResponse(WarehouseResponse Warehouse, double DistanceScore);

public sealed record ValidationError(string Field, string Message);

public static class WarehouseValidation
{
    public static ValidationError? Validate(CreateWarehouseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return new("name", "Name is required.");
        if (string.IsNullOrWhiteSpace(request.Address)) return new("address", "Address is required.");
        if (request.Capacity <= 0) return new("capacity", "Capacity must be greater than zero.");
        return null;
    }
}