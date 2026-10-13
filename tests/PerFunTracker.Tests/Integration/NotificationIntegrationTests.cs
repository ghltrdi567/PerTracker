using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Data;
using Xunit;

namespace PerFunTracker.Tests.Integration;

/// <summary>
/// Integration tests for email notification fan-out on event completion.
/// </summary>
public class NotificationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [DockerFact]
    public async Task MarkComplete_SkipsCompleter_EnqueuesForOtherParticipants()
    {
        // Arrange: create event with owner + 2 members
        var ownerEmail = $"notif-owner-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: ownerEmail,
            password: "NotifOwner123!",
            eventTitle: "Notification Test");

        var member1Email = $"notif-member1-{Guid.NewGuid():N}@example.com";
        var member1Client = await _factory.CreateParticipantClientAsync(
            eventId,
            email: member1Email,
            password: "NotifMember123!",
            displayName: "Member One",
            ownerEmail: ownerEmail);

        // Create a second member (manually, since CreateParticipantClientAsync uses default owner)
        var member2Email = $"notif-member2-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(
            email: member2Email,
            password: "NotifMember123!",
            displayName: "Member Two");

        var ownerAddClient = await _factory.CreateAuthenticatedClientAsync(
            ownerEmail, "NotifOwner123!");

        var addResponse = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = member2Email
        });
        addResponse.EnsureSuccessStatusCode();

        // Clear the notification queue before starting
        var queue = _factory.Services.GetRequiredService<IEmailNotificationQueue>();
        // Dequeue any existing messages (should be none)
        while (true)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                await queue.DequeueAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Act: member1 marks the event complete
        var completeResponse = await member1Client.PostAsJsonAsync($"/api/events/{eventId}/complete", new
        {
            note = "Test completion"
        });
        completeResponse.EnsureSuccessStatusCode();

        // Assert: completer (member1) should NOT get a notification
        // Only the owner and member2 should get notifications
        var notifiedEmails = new List<string>();
        while (true)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            try
            {
                var msg = await queue.DequeueAsync(cts.Token);
                notifiedEmails.Add(msg.ToEmail);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Owner and member2 should get notifications
        Assert.Contains(ownerEmail, notifiedEmails);
        Assert.Contains(member2Email, notifiedEmails);
        // Completer should NOT be notified
        Assert.DoesNotContain(member1Email, notifiedEmails);
    }

    [DockerFact]
    public async Task MarkComplete_RespectsNotifyByEmailFlag()
    {
        // Arrange: create event with owner who has notifications disabled
        var ownerEmail = $"optout-owner-{Guid.NewGuid():N}@example.com";
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: ownerEmail,
            password: "OptOut123!",
            eventTitle: "Opt-Out Test");

        // Disable notifications for the owner
        var settingsResponse = await ownerClient.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });
        settingsResponse.EnsureSuccessStatusCode();

        // Add a member with notifications enabled (default)
        var member1Email = $"optout-member-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(
            email: member1Email,
            password: "OptOut123!",
            displayName: "Opt-Out Member");

        var ownerAddClient = await _factory.CreateAuthenticatedClientAsync(
            ownerEmail, "OptOut123!");

        var addResponse = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = member1Email
        });
        addResponse.EnsureSuccessStatusCode();

        // Also add a member with notifications disabled
        var member2Email = $"optout-member2-{Guid.NewGuid():N}@example.com";
        await _factory.CreateUserAsync(
            email: member2Email,
            password: "OptOut123!",
            displayName: "Opt-Out Member 2");

        // Disable notifications for member2
        var member2Client = await _factory.CreateAuthenticatedClientAsync(
            member2Email, "OptOut123!");

        var member2SettingsResponse = await member2Client.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });
        member2SettingsResponse.EnsureSuccessStatusCode();

        // Add member2 to event
        var addResponse2 = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = member2Email
        });
        addResponse2.EnsureSuccessStatusCode();

        // Clear the queue
        var queue = _factory.Services.GetRequiredService<IEmailNotificationQueue>();
        while (true)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                await queue.DequeueAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Act: member1 marks complete
        var member1Client = await _factory.CreateAuthenticatedClientAsync(
            member1Email, "OptOut123!");

        var completeResponse = await member1Client.PostAsJsonAsync(
            $"/api/events/{eventId}/complete", new { note = "Test" });
        completeResponse.EnsureSuccessStatusCode();

        // Assert: all participants opted out, so no notifications should be sent
        // Owner (opt-out), member2 (opt-out) — only member1 has notifications on
        // but member1 is the completer, so they're skipped.
        var notifiedEmails = new List<string>();
        while (true)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            try
            {
                var msg = await queue.DequeueAsync(cts.Token);
                notifiedEmails.Add(msg.ToEmail);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Nobody should have been notified
        Assert.Empty(notifiedEmails);
    }
}