using Azure;
using Azure.Data.Tables;
using ServerBridge.LicensingApi.Services;
using Xunit;

namespace ServerBridge.LicensingApi.Tests;

/// <summary>
/// Exercises the real migration-completion write path (<see cref="LicenseRequestHandler"/> →
/// <see cref="TableLicenseRepository"/> → Azure Table Storage) against a local Azure Storage
/// emulator (Azurite). The tests self-skip when no emulator is reachable, so they are harmless
/// in environments without one and only run where Azurite is started (CI, or locally on demand).
/// </summary>
[Trait("Category", "Integration")]
public sealed class MigrationCompleteIntegrationTests
{
    private const string EmulatorConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "Licenses";

    /// <summary>Returns a TableClient against Azurite, or skips the test if it cannot be reached.</summary>
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
            throw; // unreachable
        }
    }

    private static void SeedActiveLicense(TableClient table, string rowKey) =>
        table.UpsertEntity(new TableEntity("license", rowKey)
        {
            ["Tier"] = "Pro",
            ["Active"] = true,
            ["DeviceId"] = "seed-device",
            ["MigrationsCompletedCount"] = 0,
        }, TableUpdateMode.Replace);

    [SkippableFact]
    public async Task ReportMigrationComplete_OnActiveLicense_StampsTimestampAndIncrementsCount()
    {
        var table = ConnectOrSkip();
        var rowKey = $"SB-IT-{Guid.NewGuid():N}";
        SeedActiveLicense(table, rowKey);

        try
        {
            var handler = new LicenseRequestHandler(new TableLicenseRepository());
            var when = DateTimeOffset.UtcNow;

            var first = await handler.ReportMigrationCompleteAsync(rowKey, "client-device", 7, when, CancellationToken.None);
            Assert.True(first.Recorded);
            Assert.Null(first.Message);

            var afterFirst = table.GetEntity<TableEntity>("license", rowKey).Value;
            Assert.Equal(1, afterFirst.GetInt32("MigrationsCompletedCount"));
            var stamp = afterFirst.GetDateTimeOffset("MigrationCompletedUtc");
            Assert.NotNull(stamp);
            Assert.True(Math.Abs((stamp!.Value - when).TotalSeconds) < 5);

            // A second completed migration increments the count again.
            var second = await handler.ReportMigrationCompleteAsync(rowKey, "client-device", 3, when.AddMinutes(1), CancellationToken.None);
            Assert.True(second.Recorded);
            Assert.Equal(2, table.GetEntity<TableEntity>("license", rowKey).Value.GetInt32("MigrationsCompletedCount"));
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task ReportMigrationComplete_OnUnknownLicense_IsRejectedAndWritesNothing()
    {
        var table = ConnectOrSkip();
        var rowKey = $"SB-IT-MISSING-{Guid.NewGuid():N}";

        var handler = new LicenseRequestHandler(new TableLicenseRepository());
        var result = await handler.ReportMigrationCompleteAsync(rowKey, "client-device", 5, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.False(result.Recorded);
        Assert.Equal("License key not found or inactive.", result.Message);

        // The handler must not create a row for an unknown key.
        var ex = Assert.Throws<RequestFailedException>(() => table.GetEntity<TableEntity>("license", rowKey));
        Assert.Equal(404, ex.Status);
    }
}
