namespace PerFunTracker.Core.Services;

/// <summary>
/// A generic outbound email: enqueued by API endpoints (registration confirmation,
/// completion notifications) and sent by the background email worker.
/// HTML is prebuilt at the enqueue site — the worker only transports it.
/// </summary>
public record EmailNotificationMessage(
    string ToEmail,
    string Subject,
    string HtmlBody);
