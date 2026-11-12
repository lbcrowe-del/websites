using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ServerBridge.LicensingApi.Services;

/// <summary>
/// Sends contact-form messages through Brevo's transactional email API, using the same
/// BREVO_API_KEY as the licence emails. The visitor is not added to any Brevo contact list:
/// the message goes to our inbox and nowhere else.
/// </summary>
public sealed class BrevoContactMailer : IContactMailer
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";
    private const string Inbox = "hello@leecrowesoftware.com";

    private static readonly HttpClient _http = new();

    private readonly ILogger<BrevoContactMailer> _logger;
    private readonly string? _apiKey;

    public BrevoContactMailer(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<BrevoContactMailer>();
        _apiKey = Environment.GetEnvironmentVariable("BREVO_API_KEY");
    }

    public async Task<bool> SendContactMessageAsync(ContactMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogError("BREVO_API_KEY is not configured — contact message not sent.");
            return false;
        }

        var who = message.Company is null ? message.Name : $"{message.Name} ({message.Company})";
        var sb = new StringBuilder().AppendLine($"From: {message.Name} <{message.Email}>");
        if (message.Company is not null) sb.AppendLine($"Company: {message.Company}");
        var text = sb.AppendLine($"Topic: {message.Topic}").AppendLine().AppendLine(message.Message).ToString();

        var html = $"""
            <p><strong>From:</strong> {Enc(message.Name)} &lt;{Enc(message.Email)}&gt;<br/>
            {(message.Company is null ? "" : $"<strong>Company:</strong> {Enc(message.Company)}<br/>")}
            <strong>Topic:</strong> {Enc(message.Topic)}</p>
            <p style="white-space:pre-wrap">{Enc(message.Message)}</p>
            <p style="color:#888;font-size:12px">Sent from the contact form on leecrowesoftware.com. Reply to answer them directly.</p>
            """;

        var payload = new
        {
            sender = new { name = "leecrowesoftware.com", email = Inbox },
            to = new[] { new { email = Inbox, name = "Lee Crowe Software" } },
            replyTo = new { email = message.Email, name = message.Name },
            subject = $"Contact form: {message.Topic} from {who}",
            htmlContent = html,
            textContent = text,
            tags = new[] { "contact-form" },
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("api-key", _apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Brevo rejected a contact message: {Status} {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Brevo contact message failed.");
            return false;
        }
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
