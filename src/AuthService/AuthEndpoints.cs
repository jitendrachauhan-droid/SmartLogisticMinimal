using System.Security.Claims;

namespace AuthService;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/register", async (RegisterRequest request, IIdentityService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RegisterAsync(request with { Role = UserRole.Customer }, cancellationToken);
            return result.Success
                ? Results.Created($"/api/auth/users/{result.User!.Id}", result.User)
                : Results.Conflict(new { message = result.Error });
        })
        .WithName("RegisterUser")
        .WithSummary("Register a user account")
        .WithDescription("Creates a customer account. Public registration cannot assign privileged roles.")
        .Produces<UserResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict)
        .ProducesValidationProblem();

        group.MapPost("/login", async (LoginRequest request, IIdentityService service, CancellationToken cancellationToken) =>
        {
            var tokens = await service.LoginAsync(request, cancellationToken);
            return tokens is null ? Results.Unauthorized() : Results.Ok(tokens);
        })
        .WithName("LoginUser")
        .WithSummary("Authenticate a user")
        .WithDescription("Validates credentials and returns a JWT access token with a refresh token.")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesValidationProblem();

        group.MapPost("/refresh", async (RefreshTokenRequest request, IIdentityService service, CancellationToken cancellationToken) =>
        {
            var tokens = await service.RefreshAsync(request.RefreshToken, cancellationToken);
            return tokens is null ? Results.Unauthorized() : Results.Ok(tokens);
        })
        .WithName("RefreshAccessToken")
        .WithSummary("Rotate a refresh token")
        .WithDescription("Consumes a refresh token once and returns a new access and refresh token pair.")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .ProducesValidationProblem();

        //group.MapGet("/me", (ClaimsPrincipal principal) => Results.Ok(new
        //{
        //    Id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"),
        //    Email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        //    Name = principal.Identity?.Name,
        //    Role = principal.FindFirstValue(ClaimTypes.Role)
        //}))
        //.RequireAuthorization()
        //.WithName("GetCurrentUser")
        //.WithSummary("Get the current authenticated user")
        //.Produces(StatusCodes.Status200OK)
        //.Produces(StatusCodes.Status401Unauthorized);

        //group.MapGet("/admin-check", () => Results.Ok(new { message = "Admin access granted." }))
        //    .RequireAuthorization("AdminOnly")
        //    .WithName("AdminAuthorizationCheck")
        //    .WithSummary("Verify administrator authorization")
        //    .Produces(StatusCodes.Status200OK)
        //    .Produces(StatusCodes.Status401Unauthorized)
        //    .Produces(StatusCodes.Status403Forbidden);
    }
}