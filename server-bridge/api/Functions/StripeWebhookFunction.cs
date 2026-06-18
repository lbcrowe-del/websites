using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ServerBridge.LicensingApi.Models;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>Handles the subset of Stripe events needed to keep self-issued license keys in sync with
/// subscription state: issue on checkout completion, deactivate on cancellation/payment failure.</summary>
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
        var dataObject = doc.RootElement.GetProperty("data").GetProperty("object");

        switch (eventType)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(dataObject, cancellationToken);
                break;
            case "customer.subscription.updated":
                await SetSubscriptionActiveAsync(dataObject, active: true, cancellationToken);
                break;
            case "customer.subscription.deleted":
                await SetSubscriptionActiveAsync(dataObject, active: false, cancellationToken);
                break;
            default:
                _logger.LogInformation("Ignoring unhandled Stripe event type {EventType}", eventType);
                break;
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    private async Task HandleCheckoutCompletedAsync(JsonElement session, CancellationToken cancellationToken)
    {
        var sessionId = session.GetProperty("id").GetString() ?? string.Empty;
        var customerId = session.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        var subscriptionId = session.TryGetProperty("subscription", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

        var licenseKey = _keyGenerator.Generate();
        var record = new LicenseRecord
        {
            RowKey = licenseKey,
            Tier = "Pro",
            Active = true,
            StripeCustomerId = customerId,
            StripeSubscriptionId = subscriptionId
        };

        await _repository.UpsertAsync(record, cancellationToken);
        await _repository.LinkCheckoutSessionAsync(sessionId, licenseKey, cancellationToken);
    }

    private async Task SetSubscriptionActiveAsync(JsonElement subscription, bool active, CancellationToken cancellationToken)
    {
        var subscriptionId = subscription.GetProperty("id").GetString();
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return;
        }

        var record = await _repository.FindByStripeSubscriptionAsync(subscriptionId, cancellationToken);
        if (record is null)
        {
            _logger.LogWarning("No license record found for Stripe subscription {SubscriptionId}", subscriptionId);
            return;
        }

        record.Active = active;
        await _repository.UpsertAsync(record, cancellationToken);
    }
}
