using Azure.Data.Tables;
using ServerBridge.LicensingApi.Services;
using Xunit;

namespace ServerBridge.LicensingApi.Tests;

/// <summary>
/// Verifies the server enforces license expiry (License Auditor keys are annual) instead of relying on
/// the client's clock, and that keys with no expiry (ServerBridge Pro) keep working. Runs against Azurite;
/// self-skips when no emulator is reachable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LicenseExpiryIntegrationTests
{
    private const string EmulatorConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "Licenses";

    private static TableClient ConnectOrSkip()
    {
        Environment.SetEnvironmentVariable("LICENSE_TABLE_CONNECTION", EmulatorConnectionString);
        try
        {
            var table = new TableClient(EmulatorConnectionString, TableName);
            table.CreateIfNotExists();
            return table;
        }
        catch (Exception ex)
        {
            Skip.If(true, $"Azure Storage emulator (Azurite) not reachable — skipping. {ex.Message}");
            throw;
        }
    }

    private static string Seed(TableClient table, string tier, string? product, DateTimeOffset? expiresAtUtc)
    {
        var rowKey = $"EXP-IT-{Guid.NewGuid():N}";
        var entity = new TableEntity("license", rowKey) { ["Tier"] = tier, ["Active"] = true };
        if (product is not null)
            entity["Product"] = product;
        if (expiresAtUtc is not null)
            entity["ExpiresAtUtc"] = expiresAtUtc.Value;
        table.UpsertEntity(entity, TableUpdateMode.Replace);
        return rowKey;
    }

    [SkippableFact]
    public async Task ExpiredAuditorKey_IsRejected_ForStatusAndActivate()
    {
        var table = ConnectOrSkip();
        var rowKey = Seed(table, "Team", "LicenseAuditor", DateTimeOffset.UtcNow.AddDays(-1));
        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());

            var status = await handler.StatusAsync(rowKey, "dev", "LicenseAuditor", CancellationToken.None);
            Assert.False(status.Valid);
            Assert.Equal("This license has expired.", status.Message);

            var activate = await handler.ActivateAsync(rowKey, "dev", null, null, "LicenseAuditor", CancellationToken.None);
            Assert.False(activate.Valid);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task UnexpiredAuditorKey_IsValid()
    {
        var table = ConnectOrSkip();
        var rowKey = Seed(table, "Starter", "LicenseAuditor", DateTimeOffset.UtcNow.AddDays(30));
        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());
            var status = await handler.StatusAsync(rowKey, "dev", "LicenseAuditor", CancellationToken.None);
            Assert.True(status.Valid);
            Assert.Equal("Starter", status.Tier);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task KeyWithNoExpiry_StaysValid()
    {
        var table = ConnectOrSkip();
        var rowKey = Seed(table, "Pro", product: null, expiresAtUtc: null); // ServerBridge Pro: perpetual
        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());
            var status = await handler.StatusAsync(rowKey, "dev", null, CancellationToken.None);
            Assert.True(status.Valid);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task WrongProduct_StaysOpaque_EvenWhenExpired()
    {
        var table = ConnectOrSkip();
        var rowKey = Seed(table, "Team", "LicenseAuditor", DateTimeOffset.UtcNow.AddDays(-1));
        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());
            var status = await handler.StatusAsync(rowKey, "dev", "ServerBridge", CancellationToken.None);
            Assert.False(status.Valid);
            Assert.Equal("License key not found or inactive.", status.Message); // don't reveal the key exists
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }
}
