using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for authentication and refresh-token rotation.
/// </summary>
public class AuthIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RefreshToken_Rotation_RevokesOldToken()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = $"rotation-{Guid.NewGuid():N}@example.com";
        const string password = "TestPass123!";

        // Register and confirm email
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            displayName = "Rotation Test"
        });
        registerResponse.EnsureSuccessStatusCode();

        // Confirm email directly
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.EmailConfirmed = true;
            await db.SaveChangesAsync();
        }

        // Login — this creates a refresh token
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password
        });
        loginResponse.EnsureSuccessStatusCode();

        var loginContent = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginContent.GetProperty("accessToken").GetString()!;

        // Get the refresh token from the cookie
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(refreshCookie);

        // Extract the refresh token value
        var refreshToken = refreshCookie!
            .Split(';')[0]
            .Replace("refreshToken=", "");

        // Act: refresh — this should rotate the token (revoke old, issue new)
        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        refreshResponse.EnsureSuccessStatusCode();

        // Assert: old token should be revoked in the database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var oldStored = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
            Assert.NotNull(oldStored);
            Assert.NotNull(oldStored!.RevokedAt); // old token should be revoked
        }
    }

    [Fact]
    public async Task RefreshToken_Reuse_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = $"reuse-{Guid.NewGuid():N}@example.com";
        const string password = "TestPass123!";

        // Register and confirm
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            displayName = "Reuse Test"
        });
        registerResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.EmailConfirmed = true;
            await db.SaveChangesAsync();
        }

        // Login
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.FirstOrDefault(c => c.StartsWith("refreshToken="));
        Assert.NotNull(refreshCookie);

        var refreshToken = refreshCookie!
            .Split(';')[0]
            .Replace("refreshToken=", "");

        // First refresh — succeeds
        var firstRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { });
        firstRefresh.EnsureSuccessStatusCode();

        // Act: try to reuse the OLD refresh token
        // The old token should now be revoked, so this should fail
        // We need to set the old cookie manually
        var reuseClient = _factory.CreateClient();
        reuseClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshToken}");

        var reuseResponse = await reuseClient.PostAsJsonAsync("/api/auth/refresh", new { });

        // Assert: reusing a revoked token returns 401
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var email = $"wrongpass-{Guid.NewGuid():N}@example.com";
        const string password = "TestPass123!";

        // Register and confirm
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            displayName = "Wrong Pass Test"
        });
        registerResponse.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == email);
            user.EmailConfirmed = true;
            await db.SaveChangesAsync();
        }

        // Act: login with wrong password
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "WrongPassword123!"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, loginResponse.StatusCode);
    }
}