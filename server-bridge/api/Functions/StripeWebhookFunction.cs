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

        var dataObject = doc.RootElement.GetProperty("data").GetProperty("object");

        if (eventType == "checkout.session.completed")
        {
            await HandleCheckoutCompletedAsync(dataObject, cancellationToken);
        }
        else if (eventType == "charge.refunded")
        {
            await HandleChargeRefundedAsync(dataObject, cancellationToken);
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
        var paymentIntentId = session.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() : null;

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
        if (!string.IsNullOrEmpty(paymentIntentId))
        {
            await _repository.LinkPaymentIntentAsync(paymentIntentId, licenseKey, cancellationToken);
        }

        _logger.LogInformation("Issued license key for Stripe session {SessionId}", sessionId);
    }

    /// <summary>Deactivates the license tied to a fully-refunded charge. Partial refunds are logged
    /// but don't deactivate — there's no partial-license concept for a flat one-time purchase.</summary>
    private async Task HandleChargeRefundedAsync(JsonElement charge, CancellationToken cancellationToken)
    {
        var chargeId = charge.TryGetProperty("id", out var cid) ? cid.GetString() : null;
        var paymentIntentId = charge.TryGetProperty("payment_intent", out var pi) && pi.ValueKind == JsonValueKind.String ? pi.GetString() : null;
        var fullyRefunded = charge.TryGetProperty("refunded", out var r) && r.GetBoolean();

        if (string.IsNullOrEmpty(paymentIntentId))
        {
            _logger.LogWarning("charge.refunded event {ChargeId} had no payment_intent; cannot locate license to deactivate.", chargeId);
            return;
        }

        if (!fullyRefunded)
        {
            _logger.LogInformation("Partial refund on charge {ChargeId}; leaving license active.", chargeId);
            return;
        }

        var licenseKey = await _repository.GetLicenseKeyForPaymentIntentAsync(paymentIntentId, cancellationToken);
        if (licenseKey is null)
        {
            _logger.LogWarning("No license found for refunded payment intent {PaymentIntentId} (charge {ChargeId}).", paymentIntentId, chargeId);
            return;
        }

        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        if (record is null)
        {
            _logger.LogWarning("License {LicenseKey} linked to payment intent {PaymentIntentId} but missing from the table.", licenseKey, paymentIntentId);
            return;
        }

        record.Active = false;
        await _repository.UpsertAsync(record, cancellationToken);
        _logger.LogInformation("Deactivated license {LicenseKey} due to full refund on charge {ChargeId}.", licenseKey, chargeId);
    }
}
