using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>
/// POST /api/contact — the contact form on leecrowesoftware.com. That site is a Free Static Web
/// App, which refuses POSTs, so the form posts here (api.server-bridge.com) instead.
///
/// Accepts form-encoded bodies (what both the page's script and a plain no-JavaScript form send;
/// neither needs a CORS preflight) or JSON. A script that asks for JSON gets
/// <c>{ ok, error }</c> back; a plain form post is redirected back to the site.
///
/// CORS is answered here, not in the Function App's platform CORS settings, which are empty.
/// If anyone ever adds origins there, the platform takes over and these headers are ignored.
/// </summary>
public sealed class ContactFunction
{
    private static readonly string[] AllowedOrigins =
    [
        "https://leecrowesoftware.com",
        "https://www.leecrowesoftware.com",
    ];

    private const string ReturnUrl = "https://leecrowesoftware.com/";

    private readonly ContactRequestHandler _handler;

    public ContactFunction(ContactRequestHandler handler) => _handler = handler;

    [Function("Contact")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "contact")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var origin = Header(req, "Origin");
        var allowedOrigin = origin is not null && AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ? origin : null;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var preflight = req.CreateResponse(HttpStatusCode.NoContent);
            AddCors(preflight, allowedOrigin);
            preflight.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            preflight.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
            preflight.Headers.Add("Access-Control-Max-Age", "86400");
            return preflight;
        }

        // A browser always sends Origin on a cross-site POST. One from somewhere else is not our form.
        if (origin is not null && allowedOrigin is null)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        var wantsJson = (Header(req, "Accept") ?? string.Empty).Contains("application/json", StringComparison.OrdinalIgnoreCase);

        ContactSubmission? submission;
        try
        {
            submission = await ReadSubmissionAsync(req, cancellationToken);
        }
        catch (JsonException)
        {
            submission = null;
        }

        var result = submission is null
            ? new ContactResult(ContactOutcome.Invalid, "The form data couldn't be read.")
            : await _handler.HandleAsync(submission, SenderKey(req), cancellationToken);

        var ok = result.Outcome is ContactOutcome.Sent or ContactOutcome.Discarded;

        if (!wantsJson)
        {
            var redirect = req.CreateResponse(HttpStatusCode.SeeOther);
            redirect.Headers.Add("Location", ReturnUrl + (ok ? "?sent=1#contact" : "?sent=0#contact"));
            return redirect;
        }

        var status = result.Outcome switch
        {
            ContactOutcome.Sent or ContactOutcome.Discarded => HttpStatusCode.OK,
            ContactOutcome.Invalid => HttpStatusCode.BadRequest,
            ContactOutcome.Throttled => HttpStatusCode.TooManyRequests,
            _ => HttpStatusCode.BadGateway,
        };
        var response = req.CreateResponse(status);
        AddCors(response, allowedOrigin);
        await response.WriteAsJsonAsync(new { ok, error = ok ? null : result.Error }, cancellationToken);
        response.StatusCode = status; // WriteAsJsonAsync resets the status to 200.
        return response;
    }

    private static async Task<ContactSubmission?> ReadSubmissionAsync(HttpRequestData req, CancellationToken cancellationToken)
    {
        var contentType = Header(req, "Content-Type") ?? string.Empty;
        using var reader = new StreamReader(req.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        if (raw.Length > 32_000) return null;

        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Deserialize<ContactSubmission>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        var form = HttpUtility.ParseQueryString(raw);
        return new ContactSubmission(
            form["name"], form["email"], form["company"], form["topic"], form["message"], form["website"],
            long.TryParse(form["elapsed"], out var elapsed) ? elapsed : null);
    }

    /// <summary>
    /// A hash of the sender's IP, used only as the in-memory throttle key. The address itself is
    /// never logged or stored. Cloudflare proxies api.server-bridge.com, so its header comes first.
    /// </summary>
    private static string SenderKey(HttpRequestData req)
    {
        var ip = Header(req, "CF-Connecting-IP")
            ?? Header(req, "X-Forwarded-For")?.Split(',')[0].Trim()
            ?? "unknown";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip)));
    }

    private static void AddCors(HttpResponseData response, string? allowedOrigin)
    {
        if (allowedOrigin is null) return;
        response.Headers.Add("Access-Control-Allow-Origin", allowedOrigin);
        response.Headers.Add("Vary", "Origin");
    }

    private static string? Header(HttpRequestData req, string name) =>
        req.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
