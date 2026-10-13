using System.Threading.Channels;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Infrastructure.Email;

/// <summary>
/// In-process queue using System.Threading.Channels.
/// Unbounded channel — no backpressure at this scale.
/// ponytail: in-proc only — messages lost on restart, works for a single API instance.
/// Upgrade path: another IEmailNotificationQueue impl (RabbitMQ) when 2+ API replicas
/// appear or lost emails start to hurt — see AGENTS.md "Roadmap status", P2 triggers.
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
