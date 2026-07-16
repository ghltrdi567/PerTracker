using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Api.BackgroundServices;

/// <summary>
/// Background service that consumes the email notification queue and sends emails.
/// Runs continuously, dequeuing messages one at a time.
/// </summary>
public class EmailNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationWorker> _logger;

    public EmailNotificationWorker(IServiceScopeFactory scopeFactory, ILogger<EmailNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email notification worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IEmailNotificationQueue>();
                var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                var msg = await queue.DequeueAsync(stoppingToken);

                var html = $"""
                    <h3>Event marked complete: {msg.EventTitle}</h3>
                    <p><strong>{msg.CompletedByName}</strong> marked this event as complete on
                       {msg.CompletedAt:yyyy-MM-dd HH:mm} UTC.</p>
                    {(msg.Note is not null ? $"<p><em>Note:</em> {msg.Note}</p>" : "")}
                    <p style="color:#888;font-size:12px;">You received this email because you participate in this event
                       and have email notifications enabled.</p>
                    """;

                await emailSender.SendAsync(msg.ToEmail, $"Event complete: {msg.EventTitle}", html);

                _logger.LogInformation("Notification sent to {Email} for event '{Event}'",
                    msg.ToEmail, msg.EventTitle);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email notification.");
            }
        }

        _logger.LogInformation("Email notification worker stopped.");
    }
}
