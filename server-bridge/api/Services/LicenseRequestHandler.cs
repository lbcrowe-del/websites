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
        string? product,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        if (record is null || !record.Active || !ProductMatches(record, product))
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
        return Valid(record.Tier, record.Product, record.ExpiresAtUtc);
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

    public async Task<LicenseResponseBody> StatusAsync(string licenseKey, string deviceId, string? product, CancellationToken cancellationToken)
    {
        var record = await _repository.GetAsync(licenseKey, cancellationToken);
        return record is { Active: true } && ProductMatches(record, product)
            ? Valid(record.Tier, record.Product, record.ExpiresAtUtc)
            : Invalid("License key not found or inactive.");
    }

    /// <summary>A key only unlocks the product it was issued for. Missing request product
    /// defaults to "ServerBridge" so existing desktop clients keep matching their own keys.</summary>
    private static bool ProductMatches(LicenseRecord record, string? requestedProduct)
    {
        var expected = string.IsNullOrWhiteSpace(requestedProduct) ? "ServerBridge" : requestedProduct;
        return string.Equals(record.Product, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static LicenseResponseBody Valid(string tier, string product, DateTimeOffset? expiresAtUtc) =>
        new(true, tier, "Stripe", expiresAtUtc, null, product);

    // Opaque on mismatch — don't reveal that the key exists for a different product.
    private static LicenseResponseBody Invalid(string message) =>
        new(false, "Free", "Stripe", null, message);
}
