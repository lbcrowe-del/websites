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
    /// (day-3, day-14, day-30 sequences) can be triggered from the Brevo UI.
    /// Implementations must never throw — log and swallow on failure.
    /// </summary>
    Task AddMarketingContactAsync(string email, string? name, string licenseKey, CancellationToken cancellationToken);
}
