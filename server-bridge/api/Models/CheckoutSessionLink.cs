using Azure;
using Azure.Data.Tables;

namespace ServerBridge.LicensingApi.Models;

/// <summary>Short-lived row mapping a Stripe Checkout Session id to the license key issued for it,
/// so the success-page redirect can retrieve the key the webhook generated.</summary>
public sealed class CheckoutSessionLink : ITableEntity
{
    public string PartitionKey { get; set; } = "session";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string LicenseKey { get; set; } = string.Empty;
}
