namespace PerFunTracker.Core.Services;

/// <summary>
/// Abstracted notification queue. In-process Channel for Phase 1;
/// can be swapped for a real queue (RabbitMQ, Redis, etc.) without touching business logic.
/// </summary>
public interface IEmailNotificationQueue
{
    /// <summary>Enqueue a notification message (non-blocking).</summary>
    ValueTask EnqueueAsync(EmailNotificationMessage message, CancellationToken ct = default);

    /// <summary>Dequeue a notification message (blocks until one is available).</summary>
    ValueTask<EmailNotificationMessage> DequeueAsync(CancellationToken ct = default);
}
