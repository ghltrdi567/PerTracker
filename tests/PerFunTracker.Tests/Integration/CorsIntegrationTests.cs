using System.Net.Http.Headers;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for CORS configuration.
/// Verifies that only allowed origins (localhost:5173, localhost:3000) get CORS headers.
/// </summary>
public class CorsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CorsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cors_AllowedOrigin_HasAllowOriginHeader()
    {
        // Arrange — simulate a request from the React dev server
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");

        // Act
        var response = await client.GetAsync("/health");

        // Assert — CORS header should be present for allowed origin
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Expected Access-Control-Allow-Origin header for allowed origin");
        var origin = response.Headers.GetValues("Access-Control-Allow-Origin").First();
        Assert.Equal("http://localhost:5173", origin);
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_NoAllowOriginHeader()
    {
        // Arrange — simulate a request from an evil site
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://evil.com");

        // Act
        var response = await client.GetAsync("/health");

        // Assert — no CORS header for disallowed origin
        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Should NOT have Access-Control-Allow-Origin for disallowed origin");
    }

    [Fact]
    public async Task Cors_Preflight_Returns200()
    {
        // Arrange — CORS preflight is an OPTIONS request
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/events");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        // Act
        var response = await client.SendAsync(request);

        // Assert — preflight should succeed
        response.EnsureSuccessStatusCode();
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Preflight response should include Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task Cors_Credentials_Allowed()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");

        // Act
        var response = await client.GetAsync("/health");

        // Assert — AllowCredentials must be true (refresh token is in HttpOnly cookie)
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Credentials"),
            "Expected Access-Control-Allow-Credentials header");
        var cred = response.Headers.GetValues("Access-Control-Allow-Credentials").First();
        Assert.Equal("true", cred);
    }
}
