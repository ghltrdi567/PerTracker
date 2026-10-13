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
///
/// Паттерн "Producer-Consumer" через Channel:
/// - Producer: EventEndpoints.MarkComplete ставит сообщения в очередь (EnqueueAsync).
/// - Consumer: этот воркер читает сообщения (DequeueAsync) и отправляет email.
/// - Очередь (ChannelEmailNotificationQueue) — in-process, unbounded.
/// - В Phase 2 можно заменить на RabbitMQ/Redis без изменения бизнес-логики.
///
/// Exponential backoff (экспоненциальная задержка):
/// - При неудаче повторяем отправку с задержками: 5с → 15с → 45с.
/// - Каждая задержка больше предыдущей, чтобы дать SMTP-серверу время восстановиться.
/// - После 3 неудач сообщение теряется (логируется как Error).
/// - В production лучше добавить dead-letter queue (DLQ) для потерянных сообщений.
/// </summary>
    public class EmailNotificationWorker : BackgroundService
    {
        // Queue — singleton, injected directly (safe from a long-lived BackgroundService).
        private readonly IEmailNotificationQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailNotificationWorker> _logger;

        private const int MaxRetries = 3;
        private static readonly TimeSpan[] RetryDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45),
        ];

        public EmailNotificationWorker(
            IEmailNotificationQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailNotificationWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Email notification worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                EmailNotificationMessage msg;
                try
                {
                    // Dequeue BEFORE creating a DI scope — otherwise the scope
                    // (with DbContext and other scoped services) sits alive
                    // during the entire idle wait for the next message.
                    msg = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    // Scope per message: IEmailSender is registered as Scoped.
                    using var scope = _scopeFactory.CreateScope();
                    var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

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
                    // Any exception here = message dropped (poison message or
                    // retries exhausted in SendWithRetryAsync). Logged with the
                    // message fields so the dropped email can be traced.
                    _logger.LogError(ex,
                        "Email notification to {Email} for event '{Event}' dropped (failed after {Retries} retries or poison message).",
                        msg.ToEmail, msg.EventTitle, MaxRetries);
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
