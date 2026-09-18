using Microsoft.Extensions.Logging;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Counts from one anonymisation pass, for logging and for the tests to assert on.</summary>
public sealed record AnonymisationRunResult(int Examined, int ClockStarted, int Anonymised, int NotDueYet);

/// <summary>
/// Erases a refunded or disputed customer's contact details 120 days after their license was
/// deactivated, in line with the retention period the privacy policy states.
///
/// What goes: CustomerName and CustomerEmail on the license row, and the Brevo marketing contact
/// (otherwise the address survives in a nurture sequence after we have erased our own copy).
/// What stays: the license key, StripeCustomerId, the EULA acceptance trail and the migration
/// counters. That linkage is what lets a chargeback, a tax question or a support request about a
/// past purchase still be answered — it is business record-keeping, not customer contact data.
///
/// The job is idempotent: a row it has already handled carries AnonymisedUtc and is skipped.
/// </summary>
public sealed class LicenseAnonymisationService
{
    /// <summary>The retention period the privacy policy commits to. Deliberately a constant and
    /// not an app setting: a mistyped setting here erases live customer data early, and the one
    /// thing this job must never do is run ahead of the promise.</summary>
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(120);

    private readonly ILicenseRepository _repository;
    private readonly IEmailService _email;
    private readonly ILogger<LicenseAnonymisationService> _logger;

    public LicenseAnonymisationService(
        ILicenseRepository repository,
        IEmailService email,
        ILoggerFactory loggerFactory)
    {
        _repository = repository;
        _email = email;
        _logger = loggerFactory.CreateLogger<LicenseAnonymisationService>();
    }

    public async Task<AnonymisationRunResult> RunAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        int examined = 0, clockStarted = 0, anonymised = 0, notDueYet = 0;

        await foreach (var record in _repository.ListDeactivatedAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            examined++;

            if (record.AnonymisedUtc is not null)
                continue;

            // Backfill for rows deactivated before DeactivatedUtc existed. We don't know when they
            // were deactivated, and Timestamp changes on every write, so guessing a date risks
            // erasing early. Start the clock now instead: these rows become eligible 120 days from
            // this pass, never sooner.
            if (record.DeactivatedUtc is null)
            {
                record.DeactivatedUtc = nowUtc;
                await _repository.UpsertAsync(record, cancellationToken);
                clockStarted++;
                _logger.LogInformation(
                    "License {LicenseKey} had no DeactivatedUtc; retention clock started at {NowUtc}.",
                    record.RowKey, nowUtc);
                continue;
            }

            if (nowUtc - record.DeactivatedUtc.Value < RetentionPeriod)
            {
                notDueYet++;
                continue;
            }

            var email = record.CustomerEmail;

            record.CustomerEmail = null;
            record.CustomerName = null;
            record.AnonymisedUtc = nowUtc;
            await _repository.UpsertAsync(record, cancellationToken);

            // Our own row is cleared first, deliberately. If Brevo is down the address is already
            // gone from the table and the next pass won't retry the delete (AnonymisedUtc is set),
            // so a Brevo failure is logged loudly rather than silently leaving our copy in place.
            if (!string.IsNullOrWhiteSpace(email))
                await _email.DeleteMarketingContactAsync(email, cancellationToken);

            anonymised++;
            _logger.LogInformation(
                "Anonymised license {LicenseKey}, deactivated {DeactivatedUtc}.",
                record.RowKey, record.DeactivatedUtc);
        }

        _logger.LogInformation(
            "Anonymisation pass complete: {Examined} deactivated rows examined, {ClockStarted} clocks started, "
            + "{Anonymised} anonymised, {NotDueYet} not due yet.",
            examined, clockStarted, anonymised, notDueYet);

        return new AnonymisationRunResult(examined, clockStarted, anonymised, notDueYet);
    }
}
