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
/// - Producer: EventEndpoints.MarkComplete и AuthEndpoints.Register ставят
///   сообщения в очередь (EnqueueAsync), HTML собирается на стороне продюсера.
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

                    await SendWithRetryAsync(emailSender, msg.ToEmail, msg.Subject, msg.HtmlBody, stoppingToken);

                    _logger.LogInformation("Email '{Subject}' sent to {Email}",
                        msg.Subject, msg.ToEmail);
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
                        "Email '{Subject}' to {Email} dropped (failed after {Retries} retries or poison message).",
                        msg.Subject, msg.ToEmail, MaxRetries);
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
