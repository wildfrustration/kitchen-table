using System.Net.Http.Json;

namespace PlanD.Api;

public sealed record EmailMessage(string To, string Subject, string Text, string? ReplyTo = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class EmailOptions
{
    /// <summary>"log" (development: write to the log) or "resend".</summary>
    public string Provider { get; set; } = "log";

    public string From { get; set; } = "Kitchen Table <no-reply@mail.kloc.io>";
    public string? ResendApiKey { get; set; }

    /// <summary>Public base URL used in links, e.g. https://kitchentable.kloc.io.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5173";
}

/// <summary>Development sender: every email goes to the log so magic links can be clicked from the console.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        log.LogInformation("EMAIL to {To}: {Subject}\n{Text}", m.To, m.Subject, m.Text);
        return Task.CompletedTask;
    }
}

/// <summary>Sends through Resend's HTTP API (https://resend.com/docs/api-reference/emails/send-email).</summary>
public sealed class ResendEmailSender(HttpClient http, EmailOptions options, ILogger<ResendEmailSender> log) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new
            {
                from = options.From,
                to = new[] { m.To },
                subject = m.Subject,
                text = m.Text,
                reply_to = m.ReplyTo,
            }),
        };
        request.Headers.Authorization = new("Bearer", options.ResendApiKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            log.LogError("Resend returned {Status}: {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }
    }
}
