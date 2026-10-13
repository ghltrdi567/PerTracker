using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for event authorization rules:
/// - Non-participants receive 404 (not 403) on event detail endpoints
/// - Members cannot delete/update events
/// - Participants can read events
/// </summary>
public class EventAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EventAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [DockerFact]
    public async Task GetEvent_NonParticipant_Returns404()
    {
        // Arrange
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);

        // Create a different user who is NOT a participant
        var strangerEmail = $"stranger-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(strangerEmail, "Stranger123!", "Stranger");
        var nonParticipantClient = await _factory.CreateAuthenticatedClientAsync(
            email: strangerEmail,
            password: "Stranger123!");

        // Act
        var response = await nonParticipantClient.GetAsync($"/api/events/{eventId}");

        // Assert: non-participants should get 404 (event existence hidden)
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [DockerFact]
    public async Task GetEvent_Participant_Returns200()
    {
        // Arrange
        var email = $"owner2-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);
        var memberClient = await _factory.CreateParticipantClientAsync(eventId,
            email: $"member1-{Guid.NewGuid():N}@example.com",
            ownerEmail: email);

        // Act
        var response = await memberClient.GetAsync($"/api/events/{eventId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(eventId.ToString(), content.GetProperty("id").GetString());
    }

    [DockerFact]
    public async Task GetEvent_Owner_Returns200()
    {
        // Arrange
        var email = $"owner3-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);

        // Act
        var response = await ownerClient.GetAsync($"/api/events/{eventId}");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [DockerFact]
    public async Task DeleteEvent_Member_Returns403()
    {
        // Arrange
        var email = $"owner4-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);
        var memberClient = await _factory.CreateParticipantClientAsync(eventId,
            email: $"member2-{Guid.NewGuid():N}@example.com",
            ownerEmail: email);

        // Act: member tries to delete the event
        var response = await memberClient.DeleteAsync($"/api/events/{eventId}");

        // Assert: only owner can delete
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task DeleteEvent_Owner_Returns204()
    {
        // Arrange
        var email = $"owner5-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);

        // Act
        var response = await ownerClient.DeleteAsync($"/api/events/{eventId}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    [DockerFact]
    public async Task UpdateEvent_Member_Returns403()
    {
        // Arrange
        var email = $"owner6-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);
        var memberClient = await _factory.CreateParticipantClientAsync(eventId,
            email: $"member3-{Guid.NewGuid():N}@example.com",
            ownerEmail: email);

        // Act: member tries to update
        var response = await memberClient.PutAsJsonAsync($"/api/events/{eventId}", new
        {
            title = "Hacked Title",
            description = "Hacked description"
        });

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task UpdateEvent_Owner_Returns200()
    {
        // Arrange
        var email = $"owner7-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);

        // Act
        var response = await ownerClient.PutAsJsonAsync($"/api/events/{eventId}", new
        {
            title = "Updated Title",
            description = "Updated description"
        });

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [DockerFact]
    public async Task MarkComplete_NonParticipant_Returns404()
    {
        // Arrange
        var email = $"owner8-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(email);
        var strangerEmail = $"stranger2-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(strangerEmail, "Stranger123!", "Stranger2");
        var strangerClient = await _factory.CreateAuthenticatedClientAsync(
            email: strangerEmail,
            password: "Stranger123!");

        // Act: non-participant tries to mark complete
        var response = await strangerClient.PostAsJsonAsync($"/api/events/{eventId}/complete", new
        {
            note = "Should not work"
        });

        // Assert: non-participant gets 404 (event hidden)
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [DockerFact]
    public async Task ListEvents_OnlyReturnsParticipatingEvents()
    {
        // Arrange: create owner with event1, add member to it
        var email = $"owner9-{Guid.NewGuid():N}@example.com";
        var (ownerClient, event1Id) = await _factory.CreateOwnedEventAsync(email, eventTitle: "Event 1");
        var memberClient = await _factory.CreateParticipantClientAsync(event1Id,
            email: $"member4-{Guid.NewGuid():N}@example.com",
            ownerEmail: email);

        // Owner creates a second event without the member
        var event2Response = await ownerClient.PostAsJsonAsync("/api/events", new
        {
            title = "Event 2 - Member NOT invited"
        });
        event2Response.EnsureSuccessStatusCode();

        // Act: member lists events
        var listResponse = await memberClient.GetAsync("/api/events?page=1&pageSize=50");
        listResponse.EnsureSuccessStatusCode();
        var content = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var items = content.GetProperty("items").EnumerateArray().ToList();

        // Assert: member should only see Event 1, not Event 2
        Assert.Single(items);
        Assert.Equal("Event 1", items[0].GetProperty("title").GetString());
    }
}