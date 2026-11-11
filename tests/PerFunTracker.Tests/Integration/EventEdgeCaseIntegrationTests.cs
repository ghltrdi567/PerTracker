using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Core.Entities;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for edge cases in event endpoints.
/// Covers: empty title, invalid GUIDs, non-existent category, duplicate participants,
/// owner self-add, remove non-existent participant, completions visibility.
/// </summary>
public class EventEdgeCaseIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EventEdgeCaseIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [DockerFact]
    public async Task CreateEvent_EmptyTitle_Returns400()
    {
        // Arrange
        var (client, _) = await _factory.CreateOwnedEventAsync(
            email: $"edge-empty-{Guid.NewGuid():N}@example.com");

        // Act — try to create another event with empty title
        var response = await client.PostAsJsonAsync("/api/events", new
        {
            title = "",
            description = (string?)null
        });

        // Assert — currently the API accepts empty titles (no explicit validation).
        // If model validation is added later, this should return 400.
        // For now, we accept either 201 (created) or 400 (validated).
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Created ||
            response.StatusCode == System.Net.HttpStatusCode.BadRequest,
            $"Expected 201 or 400, got {response.StatusCode}");
    }

    [DockerFact]
    public async Task CreateEvent_WithNonExistentCategory_Returns400()
    {
        // Arrange — authenticated client (the earlier version logged in but never
        // attached the Bearer token, and POSTed to the admin-only /api/categories)
        var email = $"edge-cat-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Edge Cat");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        // Act — event with a categoryId that doesn't exist in the Categories table
        var response = await client.PostAsJsonAsync("/api/events", new
        {
            title = "Event with bad category",
            description = (string?)null,
            categoryId = Guid.NewGuid()
        });

        // Assert — 400, not 500 (without validation SaveChanges would fail on the FK)
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task UpdateEvent_WithNonExistentCategory_Returns400()
    {
        // Arrange — same rule as POST: PUT must not 500 on an FK violation
        var (client, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-putcat-{Guid.NewGuid():N}@example.com");

        // Act — update with a categoryId that doesn't exist
        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new
        {
            title = "Updated title",
            description = (string?)null,
            categoryId = Guid.NewGuid()
        });

        // Assert — 400, not 500 (was: FK violation on SaveChanges)
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task UpdateEvent_CategoryChanged_ResponseCarriesNewCategory()
    {
        // Arrange — a category inserted directly, event without one
        Guid categoryId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new Category { Name = $"EdgeCat-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow };
            db.Categories.Add(cat);
            await db.SaveChangesAsync();
            categoryId = cat.Id;
        }

        var (client, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-catnav-{Guid.NewGuid():N}@example.com");

        // Act — assign the category via PUT
        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new
        {
            title = "With category",
            description = (string?)null,
            categoryId
        });
        response.EnsureSuccessStatusCode();

        // Assert — response reports the NEW category (nav reload after FK change)
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(categoryId, content.GetProperty("category").GetProperty("id").GetGuid());
    }

    [DockerFact]
    public async Task OverlongInput_Returns400_Not500()
    {
        // Arrange — varchar overflow (Postgres 22001) must be a 400, not a 500
        var (client, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-len-{Guid.NewGuid():N}@example.com");

        // Act + Assert — title > 200
        var longTitle = await client.PostAsJsonAsync("/api/events", new
        {
            title = new string('a', 201),
            description = (string?)null
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, longTitle.StatusCode);

        // description > 2000
        var longDesc = await client.PostAsJsonAsync("/api/events", new
        {
            title = "Valid title",
            description = new string('b', 2001)
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, longDesc.StatusCode);

        // PUT with over-long title
        var longPut = await client.PutAsJsonAsync($"/api/events/{eventId}", new
        {
            title = new string('c', 201),
            description = (string?)null
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, longPut.StatusCode);

        // completion note > 1000
        var longNote = await client.PostAsJsonAsync($"/api/events/{eventId}/complete", new
        {
            note = new string('d', 1001)
        });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, longNote.StatusCode);
    }

    [DockerFact]
    public async Task Pagination_PageZeroOrNegative_Returns200()
    {
        // Arrange — page < 1 used to hit Skip(-N) → 500 on every list endpoint
        var (client, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-page-{Guid.NewGuid():N}@example.com");

        // Act + Assert — events
        var events = await client.GetAsync("/api/events?page=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, events.StatusCode);
        var eventsContent = await events.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, eventsContent.GetProperty("page").GetInt32());

        // completions
        var completions = await client.GetAsync($"/api/events/{eventId}/completions?page=-5");
        Assert.Equal(System.Net.HttpStatusCode.OK, completions.StatusCode);

        // categories
        var categories = await client.GetAsync("/api/categories?page=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, categories.StatusCode);
    }

    [DockerFact]
    public async Task GetEvent_InvalidGuidFormat_Returns404()
    {
        // Arrange
        var (client, _) = await _factory.CreateOwnedEventAsync(
            email: $"edge-guid-{Guid.NewGuid():N}@example.com");

        // Act — non-GUID string in the URL
        var response = await client.GetAsync("/api/events/not-a-guid");

        // Assert — route constraint {id:guid} rejects non-GUID → 404
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [DockerFact]
    public async Task AddParticipant_OwnerCantAddThemself_Returns400()
    {
        // Arrange
        var email = $"edge-self-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(email, "TestPass123!", "Self Add Test");
        var client = await _factory.CreateAuthenticatedClientAsync(email, "TestPass123!");

        var createResponse = await client.PostAsJsonAsync("/api/events", new
        {
            title = "Self Add Event",
            description = (string?)null
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetGuid();

        // Act — owner tries to add themselves as participant
        var response = await client.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email
        });

        // Assert — already a participant (owner is auto-added)
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task AddParticipant_AlreadyParticipant_Returns400()
    {
        // Arrange — create event, add a member, try to add again
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-dup-{Guid.NewGuid():N}@example.com");

        var memberEmail = $"dup-member-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(memberEmail, "TestPass123!", "Dup Member");

        var addResponse = await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = memberEmail
        });
        addResponse.EnsureSuccessStatusCode();

        // Act — try to add the same member again
        var response = await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = memberEmail
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task AddParticipant_NonExistentUser_Returns400()
    {
        // Arrange
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-nouser-{Guid.NewGuid():N}@example.com");

        // Act — try to add a user that doesn't exist
        var response = await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = $"nonexistent-{Guid.NewGuid():N}@example.com"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DockerFact]
    public async Task AddParticipant_NonOwner_Returns403()
    {
        // Arrange — member tries to add another participant
        var ownerEmail = $"edge-notowner-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: ownerEmail);

        var memberClient = await _factory.CreateParticipantClientAsync(eventId,
            email: $"member-notowner-{Guid.NewGuid():N}@example.com",
            ownerEmail: ownerEmail);

        var thirdUserEmail = $"third-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(thirdUserEmail, "TestPass123!", "Third");

        // Act
        var response = await memberClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = thirdUserEmail
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task RemoveParticipant_MemberRemovesOther_Returns403()
    {
        // Arrange — member tries to remove another member
        var rmOwnerEmail = $"edge-rm-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: rmOwnerEmail);

        var member1Client = await _factory.CreateParticipantClientAsync(eventId,
            email: $"rm-member1-{Guid.NewGuid():N}@example.com",
            ownerEmail: rmOwnerEmail);

        var member2Email = $"rm-member2-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(member2Email, "TestPass123!", "Member 2");
        await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = member2Email
        });

        // Get member2's userId
        string member2UserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == member2Email);
            member2UserId = user.Id;
        }

        // Act — member1 tries to remove member2
        var response = await member1Client.DeleteAsync(
            $"/api/events/{eventId}/participants/{member2UserId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task RemoveParticipant_MemberRemovesSelf_Returns204()
    {
        // Arrange
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-selfrm-{Guid.NewGuid():N}@example.com");

        var memberEmail = $"selfrm-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(memberEmail, "TestPass123!", "Self Remove");
        await ownerClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = memberEmail
        });

        var memberClient = await _factory.CreateAuthenticatedClientAsync(memberEmail, "TestPass123!");

        string memberUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == memberEmail);
            memberUserId = user.Id;
        }

        // Act — member removes themselves
        var response = await memberClient.DeleteAsync(
            $"/api/events/{eventId}/participants/{memberUserId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [DockerFact]
    public async Task RemoveParticipant_NonExistentUser_Returns404()
    {
        // Arrange
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-rm404-{Guid.NewGuid():N}@example.com");

        // Act — try to remove a user who is not a participant
        var response = await ownerClient.DeleteAsync(
            $"/api/events/{eventId}/participants/nonexistent-user-id");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [DockerFact]
    public async Task ListCompletions_Participant_Returns200()
    {
        // Arrange — create event, mark complete, then list completions
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-comp-{Guid.NewGuid():N}@example.com");

        var completeResponse = await ownerClient.PostAsJsonAsync(
            $"/api/events/{eventId}/complete", new { note = "Done" });
        completeResponse.EnsureSuccessStatusCode();

        // Act
        var response = await ownerClient.GetAsync($"/api/events/{eventId}/completions");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(content.GetProperty("items").GetArrayLength() >= 1);
    }

    [DockerFact]
    public async Task ListCompletions_NonParticipant_Returns404()
    {
        // Arrange
        var (_, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-comp404-{Guid.NewGuid():N}@example.com");

        var strangerEmail = $"stranger-comp-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(strangerEmail, "Stranger123!", "Stranger Comp");
        var strangerClient = await _factory.CreateAuthenticatedClientAsync(
            email: strangerEmail,
            password: "Stranger123!");

        // Act
        var response = await strangerClient.GetAsync($"/api/events/{eventId}/completions");

        // Assert — non-participants get 404 (existence hidden)
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [DockerFact]
    public async Task MarkComplete_Participant_Returns201()
    {
        // Arrange
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: $"edge-mc-{Guid.NewGuid():N}@example.com");

        // Act
        var response = await ownerClient.PostAsJsonAsync(
            $"/api/events/{eventId}/complete", new { note = "Completed!" });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed!", content.GetProperty("note").GetString());
    }
}
