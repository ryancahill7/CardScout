namespace CardScout.Api.Auth;

// Outgoing email (password reset links). There is no email provider yet: swap the registration in
// Program.cs for a real implementation (SES, Postmark, SendGrid, ...) before shipping sign-up.
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default);
}

// Development stand-in: writes the email (including the link) to the API log. Outside Development it
// logs only that an email was dropped, so reset links never end up in production logs.
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    private readonly bool _includeBody;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment env)
    {
        _logger = logger;
        _includeBody = env.IsDevelopment();
    }

    public Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        if (_includeBody)
            _logger.LogWarning("DEV EMAIL (not sent) to {To}: {Subject}\n{Body}", to, subject, textBody);
        else
            _logger.LogError("No email sender configured; dropped \"{Subject}\" to a user. Configure IEmailSender.", subject);
        return Task.CompletedTask;
    }
}
