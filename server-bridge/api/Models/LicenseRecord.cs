using Azure;
using Azure.Data.Tables;

namespace ServerBridge.LicensingApi.Models;

/// <summary>Table Storage row for a self-issued (Stripe-backed) license key. PartitionKey is always "license".</summary>
public sealed class LicenseRecord : ITableEntity
{
    public string PartitionKey { get; set; } = "license";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Tier { get; set; } = "Pro";

    // Which product this key unlocks. Defaults to "ServerBridge" so legacy rows (no Product
    // column) and existing issuance resolve to ServerBridge. Auditor keys use "LicenseAuditor".
    public string Product { get; set; } = "ServerBridge";

    public bool Active { get; set; } = true;
    public string? DeviceId { get; set; }
    public string? StripeCustomerId { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    // EULA acceptance recorded at first activation. Auditable trail tying the user's
    // accepted-EULA version to their license + (via Stripe) their email. Older clients
    // that don't send these stay null.
    public string? EulaVersion { get; set; }
    public DateTimeOffset? EulaAcceptedUtc { get; set; }
    public string? EulaAcceptedFromDeviceId { get; set; }

    // Customer contact info captured from the Stripe checkout session at purchase time.
    // Blanked by the nightly anonymisation job 120 days after deactivation — see
    // LicenseAnonymisationService.
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }

    // When the key was deactivated (refund or dispute). Starts the retention clock that the
    // nightly anonymisation job counts from. Null on an active key. Rows deactivated before this
    // column existed have it stamped the first time the job sees them, so their clock starts then
    // rather than the job guessing a date it doesn't have.
    public DateTimeOffset? DeactivatedUtc { get; set; }

    // When the contact details were blanked. Doubles as the job's idempotency marker (a row with
    // this set is never touched again) and as the audit trail for the privacy policy's promise.
    public DateTimeOffset? AnonymisedUtc { get; set; }

    // Set when the client reports a completed migration. Used by the refund policy's
    // soft completed-migration check (no file names/content, just completion + count).
    public DateTimeOffset? MigrationCompletedUtc { get; set; }
    public int MigrationsCompletedCount { get; set; }
}
