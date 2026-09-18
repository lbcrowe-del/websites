namespace ServerBridge.LicensingApi.Services;

public interface IEmailService
{
    /// <summary>
    /// Sends a transactional welcome email containing the license key. The email content is
    /// tailored to <paramref name="product"/> ("ServerBridge" or "LicenseAuditor").
    /// Implementations must never throw — log and swallow on failure.
    /// </summary>
    Task SendWelcomeEmailAsync(string toEmail, string? toName, string licenseKey, string product, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a Brevo marketing contact so time-based automation
    /// (day-3, day-14, day-30 sequences) can be triggered from the Brevo UI. The
    /// contact is routed to the list for <paramref name="product"/> so each product
    /// has its own nurture stream. Implementations must never throw — log and swallow.
    /// </summary>
    Task AddMarketingContactAsync(string email, string? name, string licenseKey, string product, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the marketing contact for <paramref name="email"/>, ending every nurture sequence
    /// it is enrolled in. Called by the nightly anonymisation job once a refunded customer's
    /// retention period is up: their address is about to be erased from our table, so it must not
    /// survive in Brevo either. A contact that is already gone is not an error. Implementations
    /// must never throw — log and swallow.
    /// </summary>
    Task DeleteMarketingContactAsync(string email, CancellationToken cancellationToken);
}
