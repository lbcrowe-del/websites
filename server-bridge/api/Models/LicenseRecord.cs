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
    public bool Active { get; set; } = true;
    public string? DeviceId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
