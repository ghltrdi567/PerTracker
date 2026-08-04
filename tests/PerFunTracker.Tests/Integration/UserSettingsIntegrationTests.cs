using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for user settings endpoints (/api/users/me/settings).
/// Covers: default values, update toggle, auth requirement.
/// </summary>
public class UserSettingsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UserSettingsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetSettings_ReturnsDefaultTrue()
    {
        // Arrange — create a user and log in
        var email = $"settings-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Settings Test");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.GetAsync("/api/users/me/settings");

        // Assert — default value is true (notifications enabled)
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.GetProperty("notifyByEmailOnCompletion").GetBoolean());
    }

    [Fact]
    public async Task UpdateSettings_DisableNotification_ReturnsFalse()
    {
        // Arrange
        var email = $"disable-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Disable Test");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act — disable notifications
        var response = await client.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(content.GetProperty("notifyByEmailOnCompletion").GetBoolean());
    }

    [Fact]
    public async Task UpdateSettings_EnableNotification_ReturnsTrue()
    {
        // Arrange
        var email = $"enable-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Enable Test");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act — first disable, then re-enable
        await client.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });

        var response = await client.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = true
        });

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.GetProperty("notifyByEmailOnCompletion").GetBoolean());
    }

    [Fact]
    public async Task GetSettings_RequiresAuth_Returns401()
    {
        // Arrange — no auth header
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/users/me/settings");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
