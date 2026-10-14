using Microsoft.Extensions.Logging;
using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

/// <summary>Counts from one anonymisation pass, for logging and for the tests to assert on.</summary>
public sealed record AnonymisationRunResult(int Examined, int ClockStarted, int Anonymised, int NotDueYet);

/// <summary>
/// Erases a customer's contact details 120 days after their licence ENDED, in line with the
/// retention period both privacy policies state. A licence ends in one of two ways:
///   - it is deactivated by a refund or dispute — the clock starts at DeactivatedUtc; or
///   - it simply reaches its expiry date — the clock starts at ExpiresAtUtc.
///
/// Until 2026-10-14 only the first was handled. Nothing sets Active=false on natural expiry, so a
/// licence that just ran out was never anonymised at all — breaking the policy outright rather than
/// keeping data a little long. A licence with no expiry date never ends and is never touched.
/// 120 days, rather than less, because a card chargeback can arrive up to about 120 days after
/// purchase and answering one needs to know who paid.
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

            await AnonymiseAsync(record, nowUtc, cancellationToken);
            anonymised++;
            _logger.LogInformation(
                "Anonymised license {LicenseKey}, deactivated {DeactivatedUtc}.",
                record.RowKey, record.DeactivatedUtc);
        }

        // Second pass: licences that simply expired. Only rows already 120 days past expiry are
        // fetched, so everything returned is due; the AnonymisedUtc check keeps it idempotent.
        await foreach (var record in _repository.ListExpiredActiveAsync(nowUtc - RetentionPeriod, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            examined++;

            if (record.AnonymisedUtc is not null)
                continue;

            await AnonymiseAsync(record, nowUtc, cancellationToken);
            anonymised++;
            _logger.LogInformation(
                "Anonymised license {LicenseKey}, expired {ExpiresAtUtc}.",
                record.RowKey, record.ExpiresAtUtc);
        }

        _logger.LogInformation(
            "Anonymisation pass complete: {Examined} rows examined, {ClockStarted} clocks started, "
            + "{Anonymised} anonymised, {NotDueYet} not due yet.",
            examined, clockStarted, anonymised, notDueYet);

        return new AnonymisationRunResult(examined, clockStarted, anonymised, notDueYet);
    }

    /// <summary>Clears the contact details on one row and removes the Brevo contact. Shared by both
    /// passes so a refunded licence and an expired one are erased in exactly the same way.</summary>
    private async Task AnonymiseAsync(LicenseRecord record, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
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
    }
}
