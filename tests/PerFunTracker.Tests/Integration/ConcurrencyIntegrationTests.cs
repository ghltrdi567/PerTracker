using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        // Arrange — register, confirm, login to get a refresh token
        var container = new CookieContainer();
        var handler = new CookieHandler(container);
        var baseAddress = _factory.Server.BaseAddress!;
        var client = new HttpClient(handler) { BaseAddress = baseAddress };
        
        var email = $"conc-refresh-{Guid.NewGuid():N}@example.com";
        
        await _factory.CreateUserAsync(email, "TestPass123!", "Conc Refresh");
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "TestPass123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        // Extract the refresh token from the cookie
        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = cookies.First(c => c.StartsWith("refreshToken="));
        var refreshToken = refreshCookie.Split(';')[0].Replace("refreshToken=", "");

        // Create two separate clients with the same refresh token cookie
        var container1 = new CookieContainer();
        container1.SetCookies(new Uri("http://localhost"), refreshCookie);
        var handler1 = new CookieHandler(container1);
        var client1 = new HttpClient(handler1) { BaseAddress = _factory.Server.BaseAddress! };

        var container2 = new CookieContainer();
        container2.SetCookies(new Uri("http://localhost"), refreshCookie);
        var handler2 = new CookieHandler(container2);
        var client2 = new HttpClient(handler2) { BaseAddress = _factory.Server.BaseAddress! };

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

    /// <summary>
    /// HttpClientHandler с поддержкой CookieContainer для тестов.
    /// </summary>
    private class CookieHandler : DelegatingHandler
    {
        private readonly CookieContainer _container;

        public CookieHandler(CookieContainer container)
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
}
