using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for category endpoints.
/// Covers: list visibility (all users), create/delete (admin only), duplicates, not found.
/// </summary>
public class CategoryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CategoryIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListCategories_RegularUser_Returns200()
    {
        // Arrange — regular user (not admin)
        var email = $"cat-user-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Cat User");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.GetAsync("/api/categories");

        // Assert — all authenticated users can list categories (needed for event forms)
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateCategory_RegularUser_Returns403()
    {
        // Arrange
        var email = $"cat-noperm-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "No Perm");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act
        var response = await client.PostAsJsonAsync("/api/categories", new
        {
            name = "Should Not Work",
            icon = (string?)null
        });

        // Assert — only admins can create
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_RegularUser_Returns403()
    {
        // Arrange — create a category as admin, then try to delete as regular user
        var adminEmail = $"cat-admin-{Guid.NewGuid():N}@example.com";
        await CreateAdminAsync(_factory, adminEmail, "AdminPass123!");
        var adminClient = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");

        var createResponse = await adminClient.PostAsJsonAsync("/api/categories", new
        {
            name = "Test Category",
            icon = (string?)"📁"
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var categoryId = created.GetProperty("id").GetGuid();

        // Regular user
        var userEmail = $"cat-del-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(userEmail, "TestPass123!", "Del User");
        var userClient = await _factory.CreateAuthenticatedClientAsync(userEmail, "TestPass123!");

        // Act
        var response = await userClient.DeleteAsync($"/api/categories/{categoryId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_Admin_Returns201()
    {
        // Arrange
        var adminEmail = $"cat-create-{Guid.NewGuid():N}@example.com";
        await CreateAdminAsync(_factory, adminEmail, "AdminPass123!");
        var adminClient = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");

        // Act
        var response = await adminClient.PostAsJsonAsync("/api/categories", new
        {
            name = "Workout",
            icon = (string?)"💪"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Workout", content.GetProperty("name").GetString());
        Assert.Equal("💪", content.GetProperty("icon").GetString());
    }

    [Fact]
    public async Task CreateCategory_DuplicateName_Returns400()
    {
        // Arrange — create a category, then try to create another with the same name
        var adminEmail = $"cat-dup-{Guid.NewGuid():N}@example.com";
        await CreateAdminAsync(_factory, adminEmail, "AdminPass123!");
        var adminClient = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");

        var firstName = $"DupCat-{Guid.NewGuid():N}";
        var firstResponse = await adminClient.PostAsJsonAsync("/api/categories", new
        {
            name = firstName,
            icon = (string?)null
        });
        firstResponse.EnsureSuccessStatusCode();

        // Act — duplicate
        var response = await adminClient.PostAsJsonAsync("/api/categories", new
        {
            name = firstName,
            icon = (string?)null
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_Admin_Returns204()
    {
        // Arrange
        var adminEmail = $"cat-deladmin-{Guid.NewGuid():N}@example.com";
        await CreateAdminAsync(_factory, adminEmail, "AdminPass123!");
        var adminClient = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");

        var createResponse = await adminClient.PostAsJsonAsync("/api/categories", new
        {
            name = "ToDelete",
            icon = (string?)null
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var categoryId = created.GetProperty("id").GetGuid();

        // Act
        var response = await adminClient.DeleteAsync($"/api/categories/{categoryId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_Nonexistent_Returns404()
    {
        // Arrange
        var adminEmail = $"cat-404-{Guid.NewGuid():N}@example.com";
        await CreateAdminAsync(_factory, adminEmail, "AdminPass123!");
        var adminClient = await _factory.CreateAuthenticatedClientAsync(adminEmail, "AdminPass123!");

        // Act
        var response = await adminClient.DeleteAsync($"/api/categories/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Helper: create an admin user directly in the DB ---
    // The first user in a fresh DB becomes admin via AuthEndpoints.Register logic.
    // But since other tests may have already created users, we set IsAdmin manually.
    private static async Task CreateAdminAsync(
        CustomWebApplicationFactory factory,
        string email,
        string password)
    {
        await factory.CreateUserAsync(email, password, "Admin User");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.FirstAsync(u => u.Email == email);
        user.IsAdmin = true;
        await db.SaveChangesAsync();
    }
}
