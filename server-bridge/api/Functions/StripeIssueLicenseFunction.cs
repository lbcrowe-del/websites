using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>Backs the Stripe Payment Link's success_url (.../api/license/issue?session_id={CHECKOUT_SESSION_ID}).
/// The license key itself is created by <see cref="StripeWebhookFunction"/>; this just looks it up for display.</summary>
public sealed class StripeIssueLicenseFunction
{
    private readonly ILicenseRepository _repository;

    public StripeIssueLicenseFunction(ILicenseRepository repository) => _repository = repository;

    [Function("StripeIssueLicense")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "license/issue")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var sessionId = ParseQueryParam(req.Url.Query, "session_id");
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Missing session_id.", cancellationToken);
            return bad;
        }

        var licenseKey = await _repository.GetLicenseKeyForSessionAsync(sessionId, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");

        if (licenseKey is null)
        {
            // The webhook usually beats this redirect, but if Stripe hasn't delivered it yet, ask the
            // customer to refresh rather than show an error.
            await response.WriteStringAsync(
                "<html><body><p>Finishing setup — refresh this page in a few seconds to see your license key.</p></body></html>",
                cancellationToken);
            return response;
        }

        await response.WriteStringAsync(
            $"<html><body><p>Thanks! Your ServerBridge license key:</p><pre>{System.Net.WebUtility.HtmlEncode(licenseKey)}</pre></body></html>",
            cancellationToken);
        return response;
    }

    private static string? ParseQueryParam(string query, string name)
    {
        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
