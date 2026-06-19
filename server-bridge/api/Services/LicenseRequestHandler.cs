using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Activate/status logic behind the unified {valid, tier, provider, expiresAtUtc, message}
/// contract the desktop client expects.</summary>
public sealed class LicenseRequestHandler
{
    private readonly ILicenseRepository _repository;

    public LicenseRequestHandler(ILicenseRepository repository)
    {
        _repository = repository;
    }

    public async Task<LicenseResponseBody> ActivateAsync(string licenseKey, string deviceId, CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        if (record is null || !record.Active)
        {
            return Invalid("License key not found or inactive.");
        }

        record.DeviceId = deviceId;
        await _repository.UpsertAsync(record, cancellationToken);
        return Valid(record.Tier, record.ExpiresAtUtc);
    }

    public async Task<LicenseResponseBody> StatusAsync(string licenseKey, string deviceId, CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        return record is { Active: true }
            ? Valid(record.Tier, record.ExpiresAtUtc)
            : Invalid("License key not found or inactive.");
    }

    private static LicenseResponseBody Valid(string tier, DateTimeOffset? expiresAtUtc) =>
        new(true, tier, "Stripe", expiresAtUtc, null);

    private static LicenseResponseBody Invalid(string message) =>
        new(false, "Free", "Stripe", null, message);
}
