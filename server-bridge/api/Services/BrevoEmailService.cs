using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ServerBridge.LicensingApi.Services;

/// <summary>
/// Sends transactional email and upserts marketing contacts via the Brevo API.
///
/// Required app settings:
///   BREVO_API_KEY            — API key from Brevo → Account → SMTP &amp; API → API Keys
///   BREVO_MARKETING_LIST_ID  — integer list ID from Brevo → Contacts → Lists (create a
///                              "Pro Customers" list; copy the ID from its URL or the list table)
///
/// Brevo automation setup (one-time, in the Brevo UI):
///   1. Contacts → Automation → Create a workflow.
///   2. Trigger: "A contact is added to a specific list" → choose the list above.
///   3. Add time-delay steps:
///        Wait 3 days  → Send email template "Getting started with ServerBridge"
///        Wait 14 days → Send email template "How's your migration going?"
///        Wait 30 days → Send email template "Know someone who'd love ServerBridge?"
///   4. Each template is in EmailTemplates/day-{N}-*.html in this repo — paste the HTML
///      into Brevo's template editor and use {{ contact.FIRSTNAME }} / {{ contact.LICENSE_KEY }}.
/// </summary>
public sealed class BrevoEmailService : IEmailService
{
    private const string BaseUrl = "https://api.brevo.com/v3";
    private const string SenderEmail = "hello@leecrowesoftware.com";
    private const string SenderName = "Lee @ ServerBridge";

    // Shared singleton — HttpClient is thread-safe; reusing avoids socket exhaustion.
    private static readonly HttpClient _http = new();

    private readonly ILogger<BrevoEmailService> _logger;
    private readonly string? _apiKey;
    private readonly int? _marketingListId;

    public BrevoEmailService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<BrevoEmailService>();
        _apiKey = Environment.GetEnvironmentVariable("BREVO_API_KEY");
        var listIdRaw = Environment.GetEnvironmentVariable("BREVO_MARKETING_LIST_ID");
        _marketingListId = int.TryParse(listIdRaw, out var id) ? id : null;

        if (string.IsNullOrWhiteSpace(_apiKey))
            _logger.LogWarning("BREVO_API_KEY is not configured — emails will be skipped.");
        if (_marketingListId is null)
            _logger.LogWarning("BREVO_MARKETING_LIST_ID is not configured — marketing contacts will not be added.");
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string? toName, string licenseKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return;

        var firstName = FirstName(toName);
        var htmlBody = WelcomeHtml
            .Replace("{{CUSTOMER_NAME}}", HtmlEncode(firstName), StringComparison.Ordinal)
            .Replace("{{LICENSE_KEY}}", HtmlEncode(licenseKey), StringComparison.Ordinal);

        var payload = new
        {
            sender = new { name = SenderName, email = SenderEmail },
            to = new[] { new { email = toEmail, name = toName ?? toEmail } },
            subject = "Your ServerBridge Pro license key",
            htmlContent = htmlBody
        };

        await PostBrevoAsync("/smtp/email", payload, cancellationToken);
    }

    public async Task AddMarketingContactAsync(string email, string? name, string licenseKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return;

        var firstName = FirstName(name);
        var lastName = LastName(name);

        var payload = _marketingListId.HasValue
            ? (object)new
            {
                email,
                attributes = new
                {
                    FIRSTNAME = firstName,
                    LASTNAME = lastName,
                    LICENSE_KEY = licenseKey,
                    PURCHASE_DATE = DateTime.UtcNow.ToString("yyyy-MM-dd")
                },
                listIds = new[] { _marketingListId.Value },
                updateEnabled = true
            }
            : new
            {
                email,
                attributes = new
                {
                    FIRSTNAME = firstName,
                    LASTNAME = lastName,
                    LICENSE_KEY = licenseKey,
                    PURCHASE_DATE = DateTime.UtcNow.ToString("yyyy-MM-dd")
                },
                listIds = Array.Empty<int>(),
                updateEnabled = true
            };

        await PostBrevoAsync("/contacts", payload, cancellationToken);
    }

    private async Task PostBrevoAsync(string path, object payload, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path);
            request.Headers.Add("api-key", _apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Brevo API {Path} returned {Status}: {Body}", path, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            // Never propagate — a Brevo outage must not cause the Stripe webhook to return non-200.
            _logger.LogError(ex, "Brevo API call to {Path} failed.", path);
        }
    }

    private static string FirstName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Split(' ')[0];

    private static string LastName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var parts = fullName.Split(' ', 2);
        return parts.Length > 1 ? parts[1] : string.Empty;
    }

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    // Inline HTML welcome email — email-safe (inline styles, single-column, no CSS variables).
    // Substitution tokens: {{CUSTOMER_NAME}}, {{LICENSE_KEY}}
    private const string WelcomeHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Your ServerBridge Pro license key</title>
        </head>
        <body style="margin:0;padding:0;background:#0f1117;font-family:Arial,Helvetica,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#0f1117;">
            <tr>
              <td align="center" style="padding:40px 16px;">
                <table width="600" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;width:100%;">

                  <!-- Header -->
                  <tr>
                    <td style="background:#161b2e;border-radius:12px 12px 0 0;padding:28px 36px;">
                      <span style="font-size:22px;font-weight:700;color:#ffffff;letter-spacing:-.3px;">
                        <span style="display:inline-block;background:#5b6af0;color:#fff;border-radius:6px;
                                     width:28px;height:28px;line-height:28px;text-align:center;
                                     font-size:15px;font-weight:700;margin-right:8px;">S</span>
                        ServerBridge
                      </span>
                    </td>
                  </tr>

                  <!-- Body -->
                  <tr>
                    <td style="background:#1a1f2e;padding:36px;">

                      <p style="margin:0 0 8px;font-size:24px;font-weight:700;color:#ffffff;">
                        You're all set, {{CUSTOMER_NAME}}! &#127881;
                      </p>
                      <p style="margin:0 0 28px;font-size:15px;color:#9ca3af;line-height:1.6;">
                        Thanks for purchasing ServerBridge Pro. Here's your license key — copy it and
                        keep it somewhere safe.
                      </p>

                      <!-- License key box -->
                      <table width="100%" cellpadding="0" cellspacing="0" border="0">
                        <tr>
                          <td style="background:#0f1117;border:1px solid #3b4a6b;border-radius:8px;
                                     padding:16px 20px;font-family:monospace;font-size:15px;
                                     color:#a5b4fc;letter-spacing:.04em;word-break:break-all;">
                            {{LICENSE_KEY}}
                          </td>
                        </tr>
                      </table>

                      <!-- Activation steps -->
                      <p style="margin:32px 0 16px;font-size:15px;font-weight:600;color:#ffffff;">
                        Getting started
                      </p>
                      <table width="100%" cellpadding="0" cellspacing="0" border="0">
                        <tr>
                          <td width="36" valign="top" style="padding-bottom:14px;">
                            <div style="background:#5b6af0;color:#fff;border-radius:50%;
                                        width:26px;height:26px;line-height:26px;text-align:center;
                                        font-size:13px;font-weight:700;">1</div>
                          </td>
                          <td style="padding-bottom:14px;font-size:14px;color:#d1d5db;line-height:1.5;">
                            Download and open ServerBridge on your PC or Mac.
                          </td>
                        </tr>
                        <tr>
                          <td width="36" valign="top" style="padding-bottom:14px;">
                            <div style="background:#5b6af0;color:#fff;border-radius:50%;
                                        width:26px;height:26px;line-height:26px;text-align:center;
                                        font-size:13px;font-weight:700;">2</div>
                          </td>
                          <td style="padding-bottom:14px;font-size:14px;color:#d1d5db;line-height:1.5;">
                            In the sidebar, paste your license key into the <strong style="color:#fff;">License key</strong>
                            field and click <strong style="color:#fff;">Activate</strong>.
                          </td>
                        </tr>
                        <tr>
                          <td width="36" valign="top">
                            <div style="background:#5b6af0;color:#fff;border-radius:50%;
                                        width:26px;height:26px;line-height:26px;text-align:center;
                                        font-size:13px;font-weight:700;">3</div>
                          </td>
                          <td style="font-size:14px;color:#d1d5db;line-height:1.5;">
                            You're ready to start migrating. Have questions? Just reply to this email.
                          </td>
                        </tr>
                      </table>

                      <!-- CTA -->
                      <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin-top:32px;">
                        <tr>
                          <td>
                            <a href="https://server-bridge.com/download.html"
                               style="display:inline-block;background:#5b6af0;color:#fff;
                                      text-decoration:none;font-size:14px;font-weight:600;
                                      padding:12px 28px;border-radius:8px;">
                              Download ServerBridge
                            </a>
                          </td>
                        </tr>
                      </table>

                      <p style="margin:28px 0 0;font-size:13px;color:#6b7280;line-height:1.5;">
                        Your license key is tied to this purchase — keep it safe. A Stripe receipt
                        has also been sent separately with your payment details.
                      </p>

                    </td>
                  </tr>

                  <!-- Footer -->
                  <tr>
                    <td style="background:#161b2e;border-radius:0 0 12px 12px;padding:20px 36px;
                                border-top:1px solid #2a3147;">
                      <p style="margin:0;font-size:12px;color:#6b7280;line-height:1.6;">
                        &copy; Lee Crowe Software Solutions LLC &nbsp;&middot;&nbsp;
                        <a href="https://server-bridge.com/terms.html" style="color:#6b7280;">Terms</a>
                        &nbsp;&middot;&nbsp;
                        <a href="https://server-bridge.com/privacy.html" style="color:#6b7280;">Privacy</a>
                        &nbsp;&middot;&nbsp;
                        <a href="https://server-bridge.com/refund.html" style="color:#6b7280;">Refunds</a>
                      </p>
                    </td>
                  </tr>

                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}
