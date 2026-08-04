using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for admin endpoints (/api/admin/*).
/// Covers: admin-only access (403 for regular users), CRUD users/events, make-admin.
/// </summary>
public class AdminIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AdminIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListUsers_RegularUser_Returns403()
    {
        // Arrange
        var email = $"admin-403-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.GetAsync("/api/admin/users");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_Admin_Returns200()
    {
        // Arrange
        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.GetAsync("/api/admin/users");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.GetProperty("items").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ListUsers_Admin_SearchByEmail()
    {
        // Arrange — create a user with a unique email, then search for it
        var uniquePart = Guid.NewGuid().ToString("N");
        var targetEmail = $"searchable-{uniquePart}@example.com";
        await _factory.CreateUserAsync(targetEmail, "TestPass123!", "Searchable User");

        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.GetAsync(
            $"/api/admin/users?search={uniquePart}");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = content.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(targetEmail, items[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task ListAllEvents_RegularUser_Returns403()
    {
        // Arrange
        var email = $"events-403-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.GetAsync("/api/admin/events");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListAllEvents_Admin_Returns200()
    {
        // Arrange — create an event first so the list isn't empty
        var (adminClient, _) = await CreateAdminClientAsync();
        await adminClient.PostAsJsonAsync("/api/events", new
        {
            title = "Admin Visible Event",
            description = (string?)null
        });

        // Act
        var response = await adminClient.GetAsync("/api/admin/events");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.GetProperty("items").GetArrayLength() > 0);
    }

    [Fact]
    public async Task DeleteAnyEvent_Admin_Returns204()
    {
        // Arrange — admin creates an event, then deletes it via admin endpoint
        var (adminClient, _) = await CreateAdminClientAsync();
        var createResponse = await adminClient.PostAsJsonAsync("/api/events", new
        {
            title = "To Delete via Admin",
            description = (string?)null
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetGuid();

        // Act
        var response = await adminClient.DeleteAsync($"/api/admin/events/{eventId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAnyEvent_RegularUser_Returns403()
    {
        // Arrange
        var email = $"del-403-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.DeleteAsync($"/api/admin/events/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_Admin_RemovesUserAndOwnedEvents()
    {
        // Arrange — create a target user with an event
        var targetEmail = $"todelete-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(targetEmail, "TestPass123!", "To Delete");
        var targetClient = await _factory.CreateAuthenticatedClientAsync(targetEmail, "TestPass123!");

        var createEventResponse = await targetClient.PostAsJsonAsync("/api/events", new
        {
            title = "Owned by target user",
            description = (string?)null
        });
        createEventResponse.EnsureSuccessStatusCode();

        // Get target user ID from DB
        string targetUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == targetEmail);
            targetUserId = user.Id;
        }

        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.DeleteAsync($"/api/admin/users/{targetUserId}");

        // Assert — user is deleted
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deletedUser = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
            Assert.Null(deletedUser);

            // Owned events should also be deleted
            var ownedEvents = await db.Events
                .Where(e => e.OwnerUserId == targetUserId)
                .ToListAsync();
            Assert.Empty(ownedEvents);
        }
    }

    [Fact]
    public async Task DeleteUser_Admin_Nonexistent_Returns404()
    {
        // Arrange
        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.DeleteAsync("/api/admin/users/nonexistent-id");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_RegularUser_Returns403()
    {
        // Arrange
        var email = $"userdel-403-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.DeleteAsync("/api/admin/users/some-id");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MakeAdmin_RegularUser_Returns403()
    {
        // Arrange
        var email = $"makeadmin-403-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Regular");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/make-admin", new
        {
            email = "someone@example.com"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MakeAdmin_Admin_PromotesUser()
    {
        // Arrange — create a target user to promote
        var targetEmail = $"promote-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(targetEmail, "TestPass123!", "Promote Me");

        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.PostAsJsonAsync("/api/auth/make-admin", new
        {
            email = targetEmail
        });

        // Assert
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == targetEmail);
        Assert.True(user.IsAdmin);
    }

    [Fact]
    public async Task MakeAdmin_AlreadyAdmin_Returns400()
    {
        // Arrange — create target user and promote via DB
        var targetEmail = $"already-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(targetEmail, "TestPass123!", "Already Admin");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == targetEmail);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        }

        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.PostAsJsonAsync("/api/auth/make-admin", new
        {
            email = targetEmail
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MakeAdmin_NonexistentUser_Returns404()
    {
        // Arrange
        var (adminClient, _) = await CreateAdminClientAsync();

        // Act
        var response = await adminClient.PostAsJsonAsync("/api/auth/make-admin", new
        {
            email = $"nonexistent-{Guid.NewGuid():N}@example.com"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Helper: create an admin user and return an authenticated client ---
    private async Task<(HttpClient Client, string AdminEmail)> CreateAdminClientAsync()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(adminEmail, "AdminPass123!", "Admin User");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == adminEmail);
        user.IsAdmin = true;
        await db.SaveChangesAsync();

        var client = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");
        return (client, adminEmail);
    }
}
