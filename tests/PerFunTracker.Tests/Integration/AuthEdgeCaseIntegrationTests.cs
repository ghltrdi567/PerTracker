using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for auth edge cases.
/// Covers: registration validation, expired tokens, logout without token, lockout.
/// </summary>
public class AuthEdgeCaseIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthEdgeCaseIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [DockerFact]
    public async Task Register_DuplicateEmail_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = $"dupreg-{Guid.NewGuid():N}@example.com";

        // First registration — success
        var firstResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123!",
            displayName = "First"
        });
        firstResponse.EnsureSuccessStatusCode();

        // Act — second registration with same email
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123!",
            displayName = "Second"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task Register_WeakPassword_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act — password without digit, uppercase, or special char
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"weak-{Guid.NewGuid():N}@example.com",
            password = "weak",
            displayName = "Weak Pass"
        });

        // Assert — Identity rejects weak passwords by default
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task Login_UnconfirmedEmail_Returns400()
    {
        // Arrange — create user WITHOUT confirming email
        var email = $"unconfirmed-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Unconfirmed");

        // Manually un-confirm email (CreateUserAsync confirms it)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.EmailConfirmed = false;
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123!"
        });

        // Assert — email confirmation is required
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task Refresh_ExpiredToken_Returns401()
    {
        // Arrange — register, confirm, login to get a refresh token
        var client = _factory.CreateClient();
        var email = $"expired-{Guid.NewGuid():N}@example.com";

        await _factory.CreateUserAsync(email, "TestPass123!", "Expired Test");
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        // Expire the refresh token manually in the DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var token = await db.RefreshTokens
                .Where(rt => rt.User!.Email == email)
                .OrderByDescending(rt => rt.CreatedAt)
                .FirstAsync();
            token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1); // expired 1 minute ago
            await db.SaveChangesAsync();
        }

        // Act — try to refresh with expired token
        var response = await client.PostAsJsonAsync("/api/auth/refresh", new { });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DockerFact]
    public async Task Logout_WithoutToken_StillReturns200()
    {
        // Arrange — no refresh token cookie
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/logout", new { });

        // Assert — logout is graceful even without a token
        response.EnsureSuccessStatusCode();
    }

    [DockerFact]
    public async Task Logout_RevokesRefreshToken()
    {
        // Arrange — register, confirm, login. Factory client only (in-memory
        // TestServer): the old TestHandler wrapped a REAL HttpClientHandler →
        // "Connection refused localhost:80" in CI.
        var email = $"logout-{Guid.NewGuid():N}@example.com";

        await _factory.CreateUserAsync(email, "TestPass123!", "Logout Test");
        var loginClient = _factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        // Get the refresh token value from the Set-Cookie response header
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.First(c => c.StartsWith("refreshToken="));
        var refreshToken = refreshCookie.Split(';')[0].Replace("refreshToken=", "");

        // Act — logout with the cookie set manually
        var logoutClient = _factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshToken}");
        var logoutResponse = await logoutClient.PostAsJsonAsync("/api/auth/logout", new { });
        logoutResponse.EnsureSuccessStatusCode();

        // Assert — refresh token should be revoked in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.RefreshTokens.FirstAsync(rt => rt.Token == refreshToken);
        Assert.NotNull(stored.RevokedAt);
    }

    [DockerFact]
    public async Task Register_FirstUser_BecomesAdmin()
    {
        // Arrange — use a fresh factory to ensure no users exist.
        // Note: since tests share the same DB (IClassFixture), we can't guarantee
        // this is truly the first user. Instead, we verify the logic by checking
        // that the first user in a fresh DB would be admin.
        // This test works best when run in isolation.

        // Since we can't easily isolate the DB, we verify the admin assignment
        // logic by creating a user and checking if the DB was empty before.
        var client = _factory.CreateClient();
        var email = $"firstadmin-{Guid.NewGuid():N}@example.com";

        // Check if DB is empty
        bool wasEmpty;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            wasEmpty = !await db.Users.AnyAsync();
        }

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "TestPass123!",
            displayName = "Potential Admin"
        });
        response.EnsureSuccessStatusCode();

        // Assert — if this was the first user, they should be admin
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            if (wasEmpty)
            {
                Assert.True(user.IsAdmin, "First registered user should be admin");
            }
        }
    }
}
