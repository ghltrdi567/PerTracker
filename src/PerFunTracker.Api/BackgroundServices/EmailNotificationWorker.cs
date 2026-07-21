using System.Text.Encodings.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Api.BackgroundServices;

/// <summary>
/// Background service that consumes the email notification queue and sends emails.
/// Runs continuously, dequeuing messages one at a time.
/// Retries failed sends up to 3 times with exponential backoff.
/// </summary>
public class EmailNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationWorker> _logger;

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(45),
    ];

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
                    <h3>Event marked complete: {HtmlEncoder.Default.Encode(msg.EventTitle)}</h3>
                    <p><strong>{HtmlEncoder.Default.Encode(msg.CompletedByName)}</strong> marked this event as complete on
                       {msg.CompletedAt:yyyy-MM-dd HH:mm} UTC.</p>
                    {(msg.Note is not null ? $"<p><em>Note:</em> {HtmlEncoder.Default.Encode(msg.Note)}</p>" : "")}
                    <p style="color:#888;font-size:12px;">You received this email because you participate in this event
                       and have email notifications enabled.</p>
                    """;

                await SendWithRetryAsync(emailSender, msg.ToEmail, msg.EventTitle, html, stoppingToken);

                _logger.LogInformation("Notification sent to {Email} for event '{Event}'",
                    msg.ToEmail, msg.EventTitle);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email notification after {Retries} retries. Message dropped.",
                    MaxRetries);
            }
        }

        _logger.LogInformation("Email notification worker stopped.");
    }

    private async Task SendWithRetryAsync(
        IEmailSender emailSender,
        string toEmail,
        string eventTitle,
        string html,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await emailSender.SendAsync(toEmail, $"Event complete: {eventTitle}", html);
                return;
            }
            catch (Exception) when (attempt < MaxRetries && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Email send attempt {Attempt}/{Max} failed for '{Event}'. Retrying in {Delay}s.",
                    attempt + 1, MaxRetries, eventTitle, RetryDelays[attempt].TotalSeconds);

                await Task.Delay(RetryDelays[attempt], ct);
            }
        }
    }
}
