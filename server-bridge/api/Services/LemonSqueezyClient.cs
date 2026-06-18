using System.Text.Json;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Wraps Lemon Squeezy's public License Keys API: https://docs.lemonsqueezy.com/help/licensing/license-api
/// These endpoints are unauthenticated by design (the license key itself is the credential) and are the
/// source of truth for Lemon Squeezy-issued keys — we never store them locally.</summary>
public sealed class LemonSqueezyClient
{
    private readonly HttpClient _httpClient;

    public LemonSqueezyClient(HttpClient httpClient)
    {
        httpClient.BaseAddress = new Uri("https://api.lemonsqueezy.com/v1/licenses/");
        _httpClient = httpClient;
    }

    /// <summary>Activates the key for a device. Lemon Squeezy enforces the product's activation limit itself.</summary>
    public Task<LemonSqueezyLicenseResult?> ActivateAsync(string licenseKey, string instanceName, CancellationToken cancellationToken)
        => PostAsync("activate", new Dictionary<string, string>
        {
            ["license_key"] = licenseKey,
            ["instance_name"] = instanceName
        }, cancellationToken);

    /// <summary>Checks current validity/expiration without requiring a specific device instance.</summary>
    public Task<LemonSqueezyLicenseResult?> ValidateAsync(string licenseKey, CancellationToken cancellationToken)
        => PostAsync("validate", new Dictionary<string, string>
        {
            ["license_key"] = licenseKey
        }, cancellationToken);

    private async Task<LemonSqueezyLicenseResult?> PostAsync(
        string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var success = (root.TryGetProperty("activated", out var act) && act.GetBoolean())
            || (root.TryGetProperty("valid", out var val) && val.GetBoolean());

        if (!success || !root.TryGetProperty("license_key", out var keyElement))
        {
            return new LemonSqueezyLicenseResult(false, null, null);
        }

        var status = keyElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        DateTimeOffset? expiresAt = keyElement.TryGetProperty("expires_at", out var expEl) && expEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(expEl.GetString(), out var parsed)
                ? parsed
                : null;

        var isActive = string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
        return new LemonSqueezyLicenseResult(isActive, expiresAt, status);
    }
}

public sealed record LemonSqueezyLicenseResult(bool Valid, DateTimeOffset? ExpiresAtUtc, string? Status);
