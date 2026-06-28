namespace ServerBridge.LicensingApi.Models;

/// <summary>Unified shape returned for both Stripe- and Lemon Squeezy-issued keys. Matches the desktop client's contract.</summary>
public sealed record LicenseResponseBody(bool Valid, string Tier, string Provider, DateTimeOffset? ExpiresAtUtc, string? Message);

public sealed record LicenseRequestBody(
    string LicenseKey,
    string DeviceId,
    string? EulaVersion = null,
    DateTimeOffset? EulaAcceptedUtc = null);

public sealed record MigrationCompleteRequestBody(
    string LicenseKey,
    string DeviceId,
    int FilesMigratedCount,
    DateTimeOffset CompletedAtUtc);

public sealed record MigrationCompleteResponseBody(bool Recorded, string? Message);
