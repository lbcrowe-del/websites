using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Persistence for self-issued (Stripe-backed) license keys. Lemon Squeezy keys never touch this store —
/// their state lives in Lemon Squeezy and is queried live via <see cref="LemonSqueezyClient"/>.</summary>
public interface ILicenseRepository
{
    Task<LicenseRecord?> GetAsync(string licenseKey, CancellationToken cancellationToken);

    Task UpsertAsync(LicenseRecord record, CancellationToken cancellationToken);

    Task<LicenseRecord?> FindByStripeSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);

    /// <summary>Lets the Checkout success-page redirect (which only carries a session id) look up the
    /// license key the webhook handler issued for that same session.</summary>
    Task LinkCheckoutSessionAsync(string sessionId, string licenseKey, CancellationToken cancellationToken);

    Task<string?> GetLicenseKeyForSessionAsync(string sessionId, CancellationToken cancellationToken);
}
