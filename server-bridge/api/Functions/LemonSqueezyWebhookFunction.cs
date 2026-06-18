using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>Lemon Squeezy owns license-key issuance, emailing, and validation end-to-end via its built-in
/// License Keys API — our backend never stores Lemon Squeezy keys. This endpoint only verifies the
/// webhook signature and logs the event for visibility; there's no license state to update here.</summary>
public sealed class LemonSqueezyWebhookFunction
{
    private readonly LemonSqueezySignatureVerifier _signatureVerifier;
    private readonly ILogger<LemonSqueezyWebhookFunction> _logger;

    public LemonSqueezyWebhookFunction(LemonSqueezySignatureVerifier signatureVerifier, ILoggerFactory loggerFactory)
    {
        _signatureVerifier = signatureVerifier;
        _logger = loggerFactory.CreateLogger<LemonSqueezyWebhookFunction>();
    }

    [Function("LemonSqueezyWebhook")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/lemonsqueezy")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var payload = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
        var signatureHeader = req.Headers.TryGetValues("X-Signature", out var values) ? values.FirstOrDefault() : null;
        var webhookSecret = Environment.GetEnvironmentVariable("LEMONSQUEEZY_WEBHOOK_SECRET") ?? string.Empty;

        if (!_signatureVerifier.TryVerify(payload, signatureHeader, webhookSecret))
        {
            _logger.LogWarning("Lemon Squeezy webhook signature verification failed.");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        _logger.LogInformation("Received Lemon Squeezy webhook (signature verified, no action required).");
        return req.CreateResponse(HttpStatusCode.OK);
    }
}
