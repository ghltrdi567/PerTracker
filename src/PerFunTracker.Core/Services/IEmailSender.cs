namespace PerFunTracker.Core.Services;

/// <summary>
/// Abstraction for sending emails. Implemented by MailKitEmailSender in Infrastructure.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends an HTML email to the specified address.</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
