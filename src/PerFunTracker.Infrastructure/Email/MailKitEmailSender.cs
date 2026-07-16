using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PerFunTracker.Core.Options;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Infrastructure.Email;

/// <summary>
/// Sends emails via SMTP using MailKit. In dev/staging, points at MailHog;
/// in production, configured via env vars for a real SMTP relay.
/// </summary>
public class MailKitEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<SmtpOptions> options, ILogger<MailKitEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        // MailHog doesn't support SSL; production SMTP relay may require StartTls.
        var secureMode = _options.Username is null
            ? SecureSocketOptions.None
            : SecureSocketOptions.StartTls;

        await client.ConnectAsync(_options.Host, _options.Port, secureMode);

        if (_options.Username is not null && _options.Password is not null)
            await client.AuthenticateAsync(_options.Username, _options.Password);

        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Email sent to {ToEmail}: {Subject}", toEmail, subject);
    }
}
