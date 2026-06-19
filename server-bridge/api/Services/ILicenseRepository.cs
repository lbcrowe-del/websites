using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

public interface ILicenseRepository
{
    Task<LicenseRecord?> GetAsync(string licenseKey, CancellationToken cancellationToken);

    Task UpsertAsync(LicenseRecord record, CancellationToken cancellationToken);

    /// <summary>Stores a session-id → license-key mapping so the post-checkout redirect can look up the key.</summary>
    Task LinkCheckoutSessionAsync(string sessionId, string licenseKey, CancellationToken cancellationToken);

    Task<string?> GetLicenseKeyForSessionAsync(string sessionId, CancellationToken cancellationToken);
}
