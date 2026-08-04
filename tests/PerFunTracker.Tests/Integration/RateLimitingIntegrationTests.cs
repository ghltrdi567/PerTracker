using System.Net.Http.Json;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for rate limiting on auth endpoints.
///
/// The rate limiter is configured as: 5 requests per 1 minute, fixed window.
/// After 5 requests to any /api/auth endpoint, the 6th returns 429 Too Many Requests.
///
/// Since the window is 1 minute and we send 6 requests in quick succession,
/// all 6 hit the same window and the 6th is blocked.
/// </summary>
public class RateLimitingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RateLimitingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthEndpoint_After5Requests_Returns429()
    {
        // Arrange — register and confirm a user first
        var client = _factory.CreateClient();
        var email = $"ratelimit-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Rate Limit");

        // Send 5 requests that hit the rate limiter (login with wrong password is fast)
        for (var i = 0; i < 5; i++)
        {
            var request = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = $"wrong-{i}@example.com",
                password = "WrongPass123!"
            });
            // These can be 401 (wrong password) or 429 (rate limited) — both acceptable
        }

        // Act — 6th request should definitely be rate limited
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"wrong-6@example.com",
            password = "WrongPass123!"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task NonAuthEndpoint_NotRateLimited()
    {
        // Arrange — create an authenticated client
        var email = $"noratelimit-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "No Rate Limit");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act — send several requests to a non-auth endpoint
        var statuses = new List<System.Net.HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/events");
            statuses.Add(response.StatusCode);
        }

        // Assert — all requests succeed (no rate limiting on /api/events)
        Assert.All(statuses, s => Assert.NotEqual(System.Net.HttpStatusCode.TooManyRequests, s));
    }
}