namespace PerFunTracker.Core.Options;

/// <summary>
/// SMTP configuration bound from the "Smtp" section of appsettings.
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1025;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
}
