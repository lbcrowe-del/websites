using Azure.Data.Tables;
using ServerBridge.LicensingApi.Services;
using Xunit;

namespace ServerBridge.LicensingApi.Tests;

/// <summary>
/// Verifies that a license key only unlocks the product it was issued for, and that
/// legacy rows (no Product column) resolve to ServerBridge. Runs against Azurite;
/// self-skips when no emulator is reachable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LicenseProductIntegrationTests
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

    private static void Seed(TableClient table, string rowKey, string tier, string? product)
    {
        var entity = new TableEntity("license", rowKey) { ["Tier"] = tier, ["Active"] = true };
        if (product is not null)
            entity["Product"] = product; // omit to simulate a legacy row
        table.UpsertEntity(entity, TableUpdateMode.Replace);
    }

    [SkippableFact]
    public async Task AuditorKey_UnlocksAuditor_ButNotServerBridge()
    {
        var table = ConnectOrSkip();
        var rowKey = $"LA-IT-{Guid.NewGuid():N}";
        Seed(table, rowKey, tier: "Starter", product: "LicenseAuditor");

        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());

            var asAuditor = await handler.StatusAsync(rowKey, "dev", "LicenseAuditor", CancellationToken.None);
            Assert.True(asAuditor.Valid);
            Assert.Equal("Starter", asAuditor.Tier);
            Assert.Equal("LicenseAuditor", asAuditor.Product);

            // Same key, ServerBridge product (and the default/null case) must NOT validate.
            var asServerBridge = await handler.StatusAsync(rowKey, "dev", "ServerBridge", CancellationToken.None);
            Assert.False(asServerBridge.Valid);
            var asDefault = await handler.StatusAsync(rowKey, "dev", null, CancellationToken.None);
            Assert.False(asDefault.Valid);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task LegacyRow_NoProductColumn_ResolvesToServerBridge()
    {
        var table = ConnectOrSkip();
        var rowKey = $"SB-IT-LEGACY-{Guid.NewGuid():N}";
        Seed(table, rowKey, tier: "Pro", product: null); // legacy: no Product column

        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());

            // Default (null product) request — the existing desktop client — still works.
            var legacy = await handler.StatusAsync(rowKey, "dev", null, CancellationToken.None);
            Assert.True(legacy.Valid);
            Assert.Equal("Pro", legacy.Tier);
            Assert.Equal("ServerBridge", legacy.Product);

            // A legacy ServerBridge key cannot unlock the Auditor.
            var asAuditor = await handler.StatusAsync(rowKey, "dev", "LicenseAuditor", CancellationToken.None);
            Assert.False(asAuditor.Valid);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }
}
