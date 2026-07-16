using System.Threading.Channels;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Infrastructure.Email;

/// <summary>
/// In-process queue using System.Threading.Channels.
/// Unbounded channel — no backpressure at this scale; swap for a real queue in Phase 2.
/// </summary>
public class ChannelEmailNotificationQueue : IEmailNotificationQueue
{
    private readonly Channel<EmailNotificationMessage> _channel =
        Channel.CreateUnbounded<EmailNotificationMessage>();

    public ValueTask EnqueueAsync(EmailNotificationMessage message, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(message, ct);

    public ValueTask<EmailNotificationMessage> DequeueAsync(CancellationToken ct = default)
        => _channel.Reader.ReadAsync(ct);
}
