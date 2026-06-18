using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Shared activate/status logic behind the unified {valid, tier, provider, expiresAtUtc, message}
/// contract the desktop client expects, regardless of which payment processor issued the key.</summary>
public sealed class LicenseRequestHandler
{
    private const string SelfIssuedPrefix = "SB-";

    private readonly ILicenseRepository _repository;
    private readonly LemonSqueezyClient _lemonSqueezyClient;

    public LicenseRequestHandler(ILicenseRepository repository, LemonSqueezyClient lemonSqueezyClient)
    {
        _repository = repository;
        _lemonSqueezyClient = lemonSqueezyClient;
    }

    public async Task<LicenseResponseBody> ActivateAsync(string licenseKey, string deviceId, CancellationToken cancellationToken)
    {
        if (IsSelfIssued(licenseKey))
        {
            var record = await _repository.GetAsync(licenseKey, cancellationToken);
            if (record is null || !record.Active)
            {
                return Invalid(LicenseProviderName.Stripe, "License key not found or inactive.");
            }

            record.DeviceId = deviceId;
            await _repository.UpsertAsync(record, cancellationToken);
            return Valid(LicenseProviderName.Stripe, record.Tier, record.ExpiresAtUtc);
        }

        var result = await _lemonSqueezyClient.ActivateAsync(licenseKey, deviceId, cancellationToken);
        return result is { Valid: true }
            ? Valid(LicenseProviderName.LemonSqueezy, "Pro", result.ExpiresAtUtc)
            : Invalid(LicenseProviderName.LemonSqueezy, "License key activation failed.");
    }

    public async Task<LicenseResponseBody> StatusAsync(string licenseKey, string deviceId, CancellationToken cancellationToken)
    {
        if (IsSelfIssued(licenseKey))
        {
            var record = await _repository.GetAsync(licenseKey, cancellationToken);
            return record is { Active: true }
                ? Valid(LicenseProviderName.Stripe, record.Tier, record.ExpiresAtUtc)
                : Invalid(LicenseProviderName.Stripe, "License key not found or inactive.");
        }

        var result = await _lemonSqueezyClient.ValidateAsync(licenseKey, cancellationToken);
        return result is { Valid: true }
            ? Valid(LicenseProviderName.LemonSqueezy, "Pro", result.ExpiresAtUtc)
            : Invalid(LicenseProviderName.LemonSqueezy, "License key is no longer valid.");
    }

    private static bool IsSelfIssued(string licenseKey) => licenseKey.StartsWith(SelfIssuedPrefix, StringComparison.OrdinalIgnoreCase);

    private static LicenseResponseBody Valid(string provider, string tier, DateTimeOffset? expiresAtUtc) =>
        new(true, tier, provider, expiresAtUtc, null);

    private static LicenseResponseBody Invalid(string provider, string message) =>
        new(false, "Free", provider, null, message);
}

internal static class LicenseProviderName
{
    public const string Stripe = "Stripe";
    public const string LemonSqueezy = "LemonSqueezy";
}
