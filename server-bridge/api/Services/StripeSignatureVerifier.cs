using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Verifies the `Stripe-Signature` header per Stripe's documented scheme, without depending on the Stripe.net SDK:
/// https://docs.stripe.com/webhooks#verify-manually</summary>
public sealed class StripeSignatureVerifier
{
    private static readonly TimeSpan ToleranceWindow = TimeSpan.FromMinutes(5);

    public bool TryVerify(string payload, string? signatureHeader, string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();

        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            if (kv[0] == "t" && long.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
            {
                timestamp = t;
            }
            else if (kv[0] == "v1")
            {
                signatures.Add(kv[1]);
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            return false;
        }

        var signedPayload = $"{timestamp.Value}.{payload}";
        var expectedBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(signedPayload));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        var withinTolerance = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(timestamp.Value) < ToleranceWindow;
        return withinTolerance && signatures.Any(s => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(s), Encoding.UTF8.GetBytes(expected)));
    }
}
