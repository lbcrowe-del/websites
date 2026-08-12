namespace ServerBridge.LicensingApi.Models;

/// <summary>Unified shape returned for both Stripe- and Lemon Squeezy-issued keys. Matches the desktop client's contract.
/// Product is appended (default "None") so existing clients that ignore unknown fields keep working.</summary>
public sealed record LicenseResponseBody(bool Valid, string Tier, string Provider, DateTimeOffset? ExpiresAtUtc, string? Message, string Product = "None");

/// <summary>Product is optional; when omitted the API defaults the expected product to "ServerBridge"
/// (backward-compatible with existing desktop clients that don't send it).</summary>
public sealed record LicenseRequestBody(
    string LicenseKey,
    string DeviceId,
    string? EulaVersion = null,
    DateTimeOffset? EulaAcceptedUtc = null,
    string? Product = null);

public sealed record MigrationCompleteRequestBody(
    string LicenseKey,
    string DeviceId,
    int FilesMigratedCount,
    DateTimeOffset CompletedAtUtc);

public sealed record MigrationCompleteResponseBody(bool Recorded, string? Message);
