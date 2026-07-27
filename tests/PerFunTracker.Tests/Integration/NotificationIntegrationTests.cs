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

    [Fact]
    public async Task MarkComplete_SkipsCompleter_EnqueuesForOtherParticipants()
    {
        // Arrange: create event with owner + 2 members
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: "notif-owner@example.com",
            password: "NotifOwner123!",
            eventTitle: "Notification Test");

        var member1Client = await _factory.CreateParticipantClientAsync(
            eventId,
            email: "notif-member1@example.com",
            password: "NotifMember123!",
            displayName: "Member One");

        // Create a second member (manually, since CreateParticipantClientAsync uses default owner)
        await _factory.CreateUserAsync(
            email: "notif-member2@example.com",
            password: "NotifMember123!",
            displayName: "Member Two");

        var ownerAddClient = await _factory.CreateAuthenticatedClientAsync(
            "notif-owner@example.com", "NotifOwner123!");

        var addResponse = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = "notif-member2@example.com"
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
        Assert.Contains("notif-owner@example.com", notifiedEmails);
        Assert.Contains("notif-member2@example.com", notifiedEmails);
        // Completer should NOT be notified
        Assert.DoesNotContain("notif-member1@example.com", notifiedEmails);
    }

    [Fact]
    public async Task MarkComplete_RespectsNotifyByEmailFlag()
    {
        // Arrange: create event with owner who has notifications disabled
        var (ownerClient, eventId) = await _factory.CreateOwnedEventAsync(
            email: "optout-owner@example.com",
            password: "OptOut123!",
            eventTitle: "Opt-Out Test");

        // Disable notifications for the owner
        var settingsResponse = await ownerClient.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });
        settingsResponse.EnsureSuccessStatusCode();

        // Add a member with notifications enabled (default)
        await _factory.CreateUserAsync(
            email: "optout-member@example.com",
            password: "OptOut123!",
            displayName: "Opt-Out Member");

        var ownerAddClient = await _factory.CreateAuthenticatedClientAsync(
            "optout-owner@example.com", "OptOut123!");

        var addResponse = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = "optout-member@example.com"
        });
        addResponse.EnsureSuccessStatusCode();

        // Also add a member with notifications disabled
        await _factory.CreateUserAsync(
            email: "optout-member2@example.com",
            password: "OptOut123!",
            displayName: "Opt-Out Member 2");

        // Disable notifications for member2
        var member2Client = await _factory.CreateAuthenticatedClientAsync(
            "optout-member2@example.com", "OptOut123!");

        var member2SettingsResponse = await member2Client.PutAsJsonAsync("/api/users/me/settings", new
        {
            notifyByEmailOnCompletion = false
        });
        member2SettingsResponse.EnsureSuccessStatusCode();

        // Add member2 to event
        var addResponse2 = await ownerAddClient.PostAsJsonAsync($"/api/events/{eventId}/participants", new
        {
            email = "optout-member2@example.com"
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
            "optout-member@example.com", "OptOut123!");

        var completeResponse = await member1Client.PostAsJsonAsync(
            $"/api/events/{eventId}/complete", new { note = "Test" });
        completeResponse.EnsureSuccessStatusCode();

        // Assert: only member1 (the completer) should be skipped
        // Owner (opt-out) and member2 (opt-out) should also be skipped
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

        // Only member1 (who completed) gets no notification
        // Nobody else should have been notified since they all opted out
        Assert.Empty(notifiedEmails);
    }
}