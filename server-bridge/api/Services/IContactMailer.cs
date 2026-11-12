namespace ServerBridge.LicensingApi.Services;

/// <summary>Delivers a leecrowesoftware.com contact-form message to the company inbox.</summary>
public interface IContactMailer
{
    /// <summary>
    /// Emails <paramref name="message"/> to hello@leecrowesoftware.com with the visitor as the
    /// reply-to address. Returns false when the email was not accepted, so the form can tell the
    /// visitor to email directly instead of thinking it was sent. Never throws.
    /// </summary>
    Task<bool> SendContactMessageAsync(ContactMessage message, CancellationToken cancellationToken);
}
