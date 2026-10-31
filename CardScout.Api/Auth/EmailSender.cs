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

// Sends through Resend (https://resend.com, free tier: 3,000 emails/month, 100/day).
// Config: Email:Resend:ApiKey (user-secrets) and Email:From. Until a domain is verified in Resend,
// From must be "CardScout <onboarding@resend.dev>", which only delivers to the Resend account's own email.
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly string _from;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, string apiKey, string from, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.resend.com/");
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _from = from;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("emails", new { from = _from, to = new[] { to }, subject, text = textBody }, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend rejected an email ({Status}): {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"Email send failed ({(int)response.StatusCode}).");
        }
    }
}
