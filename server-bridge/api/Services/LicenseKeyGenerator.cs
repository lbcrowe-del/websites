using System.Security.Cryptography;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Generates self-issued license keys for Stripe purchases, formatted "SB-XXXXX-XXXXX-XXXXX-XXXXX"
/// using a base32 alphabet that excludes visually ambiguous characters (0/1/I/O).</summary>
public sealed class LicenseKeyGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int GroupCount = 4;
    private const int GroupLength = 5;

    public string Generate()
    {
        var groups = new string[GroupCount];
        for (var g = 0; g < GroupCount; g++)
        {
            groups[g] = RandomGroup();
        }

        return "SB-" + string.Join('-', groups);
    }

    private static string RandomGroup()
    {
        var chars = new char[GroupLength];
        var bytes = RandomNumberGenerator.GetBytes(GroupLength);
        for (var i = 0; i < GroupLength; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
