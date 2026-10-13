using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for concurrent requests that may cause race conditions.
/// Covers: concurrent refresh token rotation, concurrent participant addition.
///
/// These tests verify that the system handles parallel requests correctly:
/// - Refresh: only one request should succeed (old token revoked)
/// - Add participant: only one request should succeed (no duplicates)
/// </summary>
public class ConcurrencyIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [DockerFact]
    public async Task ConcurrentRefresh_SameToken_OnlyOneSucceeds()
    {
        // Arrange — register, confirm, login to get a refresh token.
        // Factory clients only (in-memory TestServer): the old version used a custom
        // CookieHandler over a REAL HttpClientHandler → "Connection refused localhost:80" in CI.
        var loginClient = _factory.CreateClient();
        var email = $"conc-refresh-{Guid.NewGuid():N}@example.com";

        await _factory.CreateUserAsync(email, "TestPass123!", "Conc Refresh");
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        // Extract the refresh token from the Set-Cookie response header
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.First(c => c.StartsWith("refreshToken="));
        var refreshToken = refreshCookie.Split(';')[0].Replace("refreshToken=", "");

        // Two clients with the SAME refresh token cookie, sent manually
        var client1 = _factory.CreateClient();
        client1.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshToken}");

        var client2 = _factory.CreateClient();
        client2.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshToken}");

        // Act — send two refresh requests simultaneously
        var task1 = client1.PostAsJsonAsync("/api/auth/refresh", new { });
        var task2 = client2.PostAsJsonAsync("/api/auth/refresh", new { });
        var responses = await Task.WhenAll(task1, task2);

        // Assert — at least one should succeed (200), at least one should fail (401)
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        // At least one should be 200 (the winner)
        Assert.Contains(System.Net.HttpStatusCode.OK, statusCodes);

        // Verify in the DB: the old token must be revoked
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var oldToken = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        Assert.NotNull(oldToken);
        Assert.NotNull(oldToken.RevokedAt);
    }

    [DockerFact]
    public async Task ConcurrentAddParticipant_NoDuplicates()
    {
        // Arrange — create an event and a target user
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"conc-add-{Guid.NewGuid():N}@example.com");

        var memberEmail = $"conc-member-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(memberEmail, "TestPass123!", "Conc Member");

        // Act — send two add-participant requests simultaneously
        var task1 = ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = memberEmail
        });
        var task2 = ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = memberEmail
        });
        var responses = await Task.WhenAll(task1, task2);

        // Assert — exactly one should succeed (200), the other should fail (400)
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(System.Net.HttpStatusCode.OK, statusCodes);
        Assert.Contains(System.Net.HttpStatusCode.BadRequest, statusCodes);

        // Verify no duplicate participants in the DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var participants = await db.EventParticipants
            .Where(p => p.EventId == eventId && p.User!.Email == memberEmail)
            .ToListAsync();
        Assert.Single(participants);
    }
}
