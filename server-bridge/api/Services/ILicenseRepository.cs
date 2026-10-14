using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

public interface ILicenseRepository
{
    Task<LicenseRecord?> GetAsync(string licenseKey, CancellationToken cancellationToken);

    Task UpsertAsync(LicenseRecord record, CancellationToken cancellationToken);

    /// <summary>Every deactivated (refunded or disputed) license row. Used by the nightly
    /// anonymisation job; active keys are never returned because they are never anonymised.</summary>
    IAsyncEnumerable<LicenseRecord> ListDeactivatedAsync(CancellationToken cancellationToken);

    /// <summary>Licences still marked Active whose expiry date is before <paramref name="expiredBeforeUtc"/>.
    /// A licence that simply runs out is never deactivated — nothing sets Active=false on natural
    /// expiry — so without this the retention job never sees it at all.</summary>
    IAsyncEnumerable<LicenseRecord> ListExpiredActiveAsync(DateTimeOffset expiredBeforeUtc, CancellationToken cancellationToken);

    /// <summary>Stores a session-id → license-key mapping so the post-checkout redirect can look up the key.</summary>
    Task LinkCheckoutSessionAsync(string sessionId, string licenseKey, CancellationToken cancellationToken);

    Task<string?> GetLicenseKeyForSessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Stores a payment-intent-id → license-key mapping so a later refund webhook can find
    /// the license to deactivate (refund events carry the charge/payment intent, not the checkout session).</summary>
    Task LinkPaymentIntentAsync(string paymentIntentId, string licenseKey, CancellationToken cancellationToken);

    Task<string?> GetLicenseKeyForPaymentIntentAsync(string paymentIntentId, CancellationToken cancellationToken);
}
