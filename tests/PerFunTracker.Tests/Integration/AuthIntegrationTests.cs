using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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

/// <summary>
/// Создает HttpClient с контейнером кук для тестирования refresh-токенов.
/// </summary>
private HttpClient CreateClientWithCookies()
{
    var container = new CookieContainer();
    var handler = new TestHandler(container);
    var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });
    client.Dispose();
    var baseAddress = _factory.Server.BaseAddress!;
    return new HttpClient(handler) { BaseAddress = baseAddress };
}

/// <summary>
/// HttpClientHandler с поддержкой CookieContainer для тестов refresh-токенов.
/// Автоматически добавляет куки в запросы и извлекает из ответов.
/// </summary>
private class TestHandler : DelegatingHandler
{
    private readonly CookieContainer _container;

    public TestHandler(CookieContainer container)
    {
        _container = container;
        InnerHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseDefaultCredentials = true
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var cookies = _container.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookies))
        {
            request.Headers.Add("Cookie", cookies);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                _container.SetCookies(uri, setCookie);
            }
        }

        return response;
    }
}

    [DockerFact]
    public async Task RefreshToken_Rotation_RevokesOldToken()
    {
        // Arrange
        var client = CreateClientWithCookies();
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
        // Note: HandleCookies=false ensures Set-Cookie headers are NOT consumed by the CookieContainerHandler
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

    [DockerFact]
    public async Task RefreshToken_Reuse_ReturnsUnauthorized()
    {
        // Arrange
        var client = CreateClientWithCookies();
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

    [DockerFact]
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