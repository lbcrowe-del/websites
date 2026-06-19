using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ServerBridge.LicensingApi.Models;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>Issues a license key when a Stripe one-time checkout completes.</summary>
public sealed class StripeWebhookFunction
{
    private readonly ILicenseRepository _repository;
    private readonly LicenseKeyGenerator _keyGenerator;
    private readonly StripeSignatureVerifier _signatureVerifier;
    private readonly ILogger<StripeWebhookFunction> _logger;

    public StripeWebhookFunction(
        ILicenseRepository repository,
        LicenseKeyGenerator keyGenerator,
        StripeSignatureVerifier signatureVerifier,
        ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _keyGenerator = keyGenerator;
        _signatureVerifier = signatureVerifier;
        _logger = loggerFactory.CreateLogger<StripeWebhookFunction>();
    }

    [Function("StripeWebhook")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/stripe")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var payload = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
        var signatureHeader = req.Headers.TryGetValues("Stripe-Signature", out var values) ? values.FirstOrDefault() : null;
        var webhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? string.Empty;

        if (!_signatureVerifier.TryVerify(payload, signatureHeader, webhookSecret))
        {
            _logger.LogWarning("Stripe webhook signature verification failed.");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        using var doc = JsonDocument.Parse(payload);
        var eventType = doc.RootElement.GetProperty("type").GetString();

        if (eventType == "checkout.session.completed")
        {
            var dataObject = doc.RootElement.GetProperty("data").GetProperty("object");
            await HandleCheckoutCompletedAsync(dataObject, cancellationToken);
        }
        else
        {
            _logger.LogInformation("Ignoring unhandled Stripe event type {EventType}", eventType);
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    private async Task HandleCheckoutCompletedAsync(JsonElement session, CancellationToken cancellationToken)
    {
        var sessionId = session.GetProperty("id").GetString() ?? string.Empty;
        var customerId = session.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;

        var licenseKey = _keyGenerator.Generate();
        var record = new LicenseRecord
        {
            RowKey = licenseKey,
            Tier = "Pro",
            Active = true,
            StripeCustomerId = customerId
        };

        await _repository.UpsertAsync(record, cancellationToken);
        await _repository.LinkCheckoutSessionAsync(sessionId, licenseKey, cancellationToken);
        _logger.LogInformation("Issued license key for Stripe session {SessionId}", sessionId);
    }
}
