using AuthService;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Tests;

public class UnitTest1
{
    [Fact]
    public void Password_hashes_are_salted_and_verify_correct_password_only()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var firstHash = hasher.Hash("CorrectPassword123!");
        var secondHash = hasher.Hash("CorrectPassword123!");

        Assert.NotEqual(firstHash, secondHash);
        Assert.True(hasher.Verify("CorrectPassword123!", firstHash));
        Assert.False(hasher.Verify("WrongPassword123!", firstHash));
    }

    [Fact]
    public async Task Login_returns_jwt_and_refresh_token_can_only_be_used_once()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new AuthDbContext(options);
        var identity = new IdentityService(
            dbContext,
            new JwtOptions { SigningKey = "f5PF60kaR7yVDSPH4kZ5ZxxI9l5Zd6fcfILS+veDfRE=" },
            new Pbkdf2PasswordHasher());

        var registration = await identity.RegisterAsync(new RegisterRequest
        {
            Email = "admin@example.com",
            Password = "CorrectPassword123!",
            DisplayName = "Operations Admin",
            Role = UserRole.Admin
        }, CancellationToken.None);

        var tokens = await identity.LoginAsync(new LoginRequest
        {
            Email = "admin@example.com",
            Password = "CorrectPassword123!"
        }, CancellationToken.None);

        Assert.True(registration.Success);
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens.AccessToken);
        Assert.Equal(UserRole.Admin, tokens.User.Role);

        var rotatedTokens = await identity.RefreshAsync(tokens.RefreshToken, CancellationToken.None);
        var replayedTokens = await identity.RefreshAsync(tokens.RefreshToken, CancellationToken.None);

        Assert.NotNull(rotatedTokens);
        Assert.Null(replayedTokens);
    }
}
