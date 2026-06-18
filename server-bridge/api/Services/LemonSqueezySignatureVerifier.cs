using System.Security.Cryptography;
using System.Text;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Verifies the `X-Signature` header Lemon Squeezy sends with webhook requests (HMAC-SHA256 of the raw body):
/// https://docs.lemonsqueezy.com/help/webhooks#signing-requests</summary>
public sealed class LemonSqueezySignatureVerifier
{
    public bool TryVerify(string payload, string? signatureHeader, string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader))
        {
            return false;
        }

        var expectedBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret), Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(expectedBytes).ToLowerInvariant();

        if (signatureHeader.Length != expected.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected));
    }
}
