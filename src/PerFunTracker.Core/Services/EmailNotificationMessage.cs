namespace PerFunTracker.Core.Services;

/// <summary>
/// A message enqueued when an event is marked complete.
/// Consumed by the background email notifier.
/// </summary>
public record EmailNotificationMessage(
    string ToEmail,
    string ToDisplayName,
    string EventTitle,
    string CompletedByName,
    DateTime CompletedAt,
    string? Note);
