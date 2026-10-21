using System.ComponentModel.DataAnnotations;

namespace AuthService;

public enum UserRole
{
    Admin,
    OperationsManager,
    WarehouseManager,
    DeliveryAgent,
    Customer
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; init; } = "SmartLogistics.AuthService";
    public string Audience { get; init; } = "SmartLogistics.Api";
    public string SigningKey { get; init; } = "f5PF60kaR7yVDSPH4kZ5ZxxI9l5Zd6fcfILS+veDfRE=";
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 7;
}

/// <summary>Payload for creating a user account.</summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    [Required, MaxLength(120)]
    public required string DisplayName { get; init; }

    public UserRole Role { get; init; } = UserRole.Customer;
}

/// <summary>Payload for obtaining access and refresh tokens.</summary>
public sealed record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

/// <summary>Payload for rotating a refresh token.</summary>
public sealed record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

/// <summary>Authenticated user information returned by the API.</summary>
public sealed record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role);

/// <summary>JWT access token and one-time refresh token pair.</summary>
public sealed record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, UserResponse User);

public sealed record StoredUser(Guid Id, string Email, string DisplayName, UserRole Role, string PasswordHash, DateTimeOffset CreatedAt);

public sealed record StoredRefreshToken(Guid UserId, string TokenHash, DateTimeOffset ExpiresAt, bool Revoked);