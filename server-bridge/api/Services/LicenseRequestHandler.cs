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

    public async Task<LicenseResponseBody> ActivateAsync(
        string licenseKey,
        string deviceId,
        string? eulaVersion,
        DateTimeOffset? eulaAcceptedUtc,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        if (record is null || !record.Active)
        {
            return Invalid("License key not found or inactive.");
        }

        record.DeviceId = deviceId;

        // Record the EULA acceptance the first time we see one for this license. Once stored,
        // it stays put — a later activation on a different device doesn't overwrite the original
        // audit record. Older clients (no eulaVersion) simply leave the row untouched.
        if (!string.IsNullOrWhiteSpace(eulaVersion) && record.EulaVersion is null)
        {
            record.EulaVersion = eulaVersion;
            record.EulaAcceptedUtc = eulaAcceptedUtc ?? DateTimeOffset.UtcNow;
            record.EulaAcceptedFromDeviceId = deviceId;
        }

        await _repository.UpsertAsync(record, cancellationToken);
        return Valid(record.Tier, record.ExpiresAtUtc);
    }

    public async Task<MigrationCompleteResponseBody> ReportMigrationCompleteAsync(
        string licenseKey,
        string deviceId,
        int filesMigratedCount,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        if (record is null || !record.Active)
        {
            return new MigrationCompleteResponseBody(false, "License key not found or inactive.");
        }

        record.MigrationCompletedUtc = completedAtUtc;
        record.MigrationsCompletedCount += 1;

        await _repository.UpsertAsync(record, cancellationToken);
        return new MigrationCompleteResponseBody(true, null);
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
