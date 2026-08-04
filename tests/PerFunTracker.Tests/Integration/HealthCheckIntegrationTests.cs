using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for the health check endpoint.
/// Health check is used by Docker/container orchestrators to verify the API is alive.
/// </summary>
public class HealthCheckIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthCheckIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Health_DoesNotRequireAuth()
    {
        // Arrange — no Authorization header, no token
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert: health check must be accessible without authentication
        // (Docker/CI probes don't have JWT tokens)
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
