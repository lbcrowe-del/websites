using Azure.Data.Tables;
using Microsoft.Extensions.Logging.Abstractions;
using ServerBridge.LicensingApi.Models;
using ServerBridge.LicensingApi.Services;
using Xunit;

namespace ServerBridge.LicensingApi.Tests;

/// <summary>
/// Verifies the nightly retention job: a refunded customer's name and email are erased 120 days
/// after deactivation and their Brevo contact is removed, while the license key and payment
/// linkage survive — and, just as importantly, that nothing is erased a day early. Runs against
/// Azurite; self-skips when no emulator is reachable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LicenseAnonymisationIntegrationTests
{
    private const string EmulatorConnectionString = "UseDevelopmentStorage=true";
    private const string TableName = "Licenses";

    /// <summary>Records the addresses it was asked to delete, so a test can assert Brevo was
    /// (or was not) told to drop the contact without touching the live Brevo tenant.</summary>
    private sealed class RecordingEmailService : IEmailService
    {
        public List<string> Deleted { get; } = [];

        public Task SendWelcomeEmailAsync(string toEmail, string? toName, string licenseKey, string product, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AddMarketingContactAsync(string email, string? name, string licenseKey, string product, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteMarketingContactAsync(string email, CancellationToken cancellationToken)
        {
            Deleted.Add(email);
            return Task.CompletedTask;
        }
    }

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

    private static string Seed(TableClient table, bool active, DateTimeOffset? deactivatedUtc, DateTimeOffset? anonymisedUtc = null)
    {
        var rowKey = $"ANON-IT-{Guid.NewGuid():N}";
        var entity = new TableEntity("license", rowKey)
        {
            ["Tier"] = "Pro",
            ["Product"] = "ServerBridge",
            ["Active"] = active,
            ["CustomerEmail"] = $"{rowKey}@example.test",
            ["CustomerName"] = "Test Person",
            ["StripeCustomerId"] = "cus_test_" + rowKey,
            ["EulaVersion"] = "1.4",
        };
        if (deactivatedUtc is not null)
            entity["DeactivatedUtc"] = deactivatedUtc.Value;
        if (anonymisedUtc is not null)
            entity["AnonymisedUtc"] = anonymisedUtc.Value;
        table.UpsertEntity(entity, TableUpdateMode.Replace);
        return rowKey;
    }

    private static (LicenseAnonymisationService Service, RecordingEmailService Email) CreateService()
    {
        var email = new RecordingEmailService();
        return (new LicenseAnonymisationService(new TableLicenseRepository(), email, NullLoggerFactory.Instance), email);
    }

    [SkippableFact]
    public async Task RefundedLicense_PastRetention_LosesContactDetailsButKeepsPaymentLinkage()
    {
        var table = ConnectOrSkip();
        var now = DateTimeOffset.UtcNow;
        var rowKey = Seed(table, active: false, deactivatedUtc: now - TimeSpan.FromDays(121));
        try
        {
            var (service, email) = CreateService();

            var result = await service.RunAsync(now, CancellationToken.None);
            Assert.True(result.Anonymised >= 1);

            var row = table.GetEntity<LicenseRecord>("license", rowKey).Value;
            Assert.Null(row.CustomerEmail);
            Assert.Null(row.CustomerName);
            Assert.NotNull(row.AnonymisedUtc);

            // The business record survives: the key itself, who paid, and the EULA trail.
            Assert.Equal(rowKey, row.RowKey);
            Assert.Equal("cus_test_" + rowKey, row.StripeCustomerId);
            Assert.Equal("1.4", row.EulaVersion);

            Assert.Contains($"{rowKey}@example.test", email.Deleted);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task RefundedLicense_InsideRetention_IsLeftAlone()
    {
        var table = ConnectOrSkip();
        var now = DateTimeOffset.UtcNow;
        // One day short of the promise. This is the assertion that matters most: erasing early is
        // worse than erasing late, because it is not recoverable.
        var rowKey = Seed(table, active: false, deactivatedUtc: now - TimeSpan.FromDays(119));
        try
        {
            var (service, email) = CreateService();

            await service.RunAsync(now, CancellationToken.None);

            var row = table.GetEntity<LicenseRecord>("license", rowKey).Value;
            Assert.Equal($"{rowKey}@example.test", row.CustomerEmail);
            Assert.Equal("Test Person", row.CustomerName);
            Assert.Null(row.AnonymisedUtc);
            Assert.DoesNotContain($"{rowKey}@example.test", email.Deleted);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task ActiveLicense_IsNeverTouched()
    {
        var table = ConnectOrSkip();
        var now = DateTimeOffset.UtcNow;
        // An active key with an ancient deactivation date — nonsense state, but if the job ever
        // keyed off the date alone instead of Active it would erase a paying customer's details.
        var rowKey = Seed(table, active: true, deactivatedUtc: now - TimeSpan.FromDays(400));
        try
        {
            var (service, email) = CreateService();

            await service.RunAsync(now, CancellationToken.None);

            var row = table.GetEntity<LicenseRecord>("license", rowKey).Value;
            Assert.Equal($"{rowKey}@example.test", row.CustomerEmail);
            Assert.Null(row.AnonymisedUtc);
            Assert.Empty(email.Deleted);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task LegacyRow_WithNoDeactivationDate_StartsTheClockAndIsNotErasedThisPass()
    {
        var table = ConnectOrSkip();
        var now = DateTimeOffset.UtcNow;
        var rowKey = Seed(table, active: false, deactivatedUtc: null);
        try
        {
            var (service, email) = CreateService();

            var first = await service.RunAsync(now, CancellationToken.None);
            Assert.True(first.ClockStarted >= 1);

            var row = table.GetEntity<LicenseRecord>("license", rowKey).Value;
            Assert.NotNull(row.DeactivatedUtc);
            Assert.Equal($"{rowKey}@example.test", row.CustomerEmail);
            Assert.Null(row.AnonymisedUtc);
            Assert.DoesNotContain($"{rowKey}@example.test", email.Deleted);

            // The backfilled date is honoured: 119 days later it is still not due.
            await service.RunAsync(now + TimeSpan.FromDays(119), CancellationToken.None);
            Assert.NotNull(table.GetEntity<LicenseRecord>("license", rowKey).Value.CustomerEmail);

            // 121 days later it is.
            await service.RunAsync(now + TimeSpan.FromDays(121), CancellationToken.None);
            Assert.Null(table.GetEntity<LicenseRecord>("license", rowKey).Value.CustomerEmail);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }

    [SkippableFact]
    public async Task AlreadyAnonymisedRow_IsNotProcessedAgain()
    {
        var table = ConnectOrSkip();
        var now = DateTimeOffset.UtcNow;
        var anonymisedAt = now - TimeSpan.FromDays(10);
        var rowKey = Seed(table, active: false, deactivatedUtc: now - TimeSpan.FromDays(200), anonymisedUtc: anonymisedAt);
        try
        {
            var (service, email) = CreateService();

            await service.RunAsync(now, CancellationToken.None);

            var row = table.GetEntity<LicenseRecord>("license", rowKey).Value;
            // Untouched: the marker still holds the original date, and Brevo was not called again
            // for an address we erased months ago.
            Assert.Equal(anonymisedAt.ToUnixTimeSeconds(), row.AnonymisedUtc!.Value.ToUnixTimeSeconds());
            Assert.Empty(email.Deleted);
        }
        finally
        {
            table.DeleteEntity("license", rowKey);
        }
    }
}
