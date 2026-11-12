using System.Collections.Concurrent;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace ServerBridge.LicensingApi.Services;

/// <summary>What a visitor typed into the leecrowesoftware.com contact form, unvalidated.</summary>
/// <param name="Website">Honeypot. The field is hidden from people, so anything in it came from a bot.</param>
/// <param name="ElapsedMs">How long the form was open before it was sent, measured by the page's
/// script. Null when the visitor has no JavaScript, which is allowed.</param>
public sealed record ContactSubmission(
    string? Name,
    string? Email,
    string? Company,
    string? Topic,
    string? Message,
    string? Website,
    long? ElapsedMs);

/// <summary>A submission that passed validation, ready to email.</summary>
public sealed record ContactMessage(string Name, string Email, string? Company, string Topic, string Message);

public enum ContactOutcome
{
    /// <summary>Emailed to hello@.</summary>
    Sent,
    /// <summary>Looks automated. Reported to the sender as success so a bot learns nothing.</summary>
    Discarded,
    /// <summary>A field is missing or malformed; <see cref="ContactResult.Error"/> says which.</summary>
    Invalid,
    /// <summary>Too many messages from one address in the last hour.</summary>
    Throttled,
    /// <summary>Brevo did not accept the email.</summary>
    Failed,
}

public sealed record ContactResult(ContactOutcome Outcome, string? Error = null);

/// <summary>
/// Validates a contact-form submission, filters obvious bots and floods, and hands the rest to
/// <see cref="IContactMailer"/>. Kept apart from the HTTP function so it can be tested directly.
///
/// Nothing here is stored. The per-sender throttle lives in memory, keyed on a hash of the
/// sender's IP address, and resets whenever the Functions host recycles. It is a speed bump for
/// floods, not a guarantee; the honeypot and the minimum fill time stop most form bots.
/// </summary>
public sealed class ContactRequestHandler
{
    /// <summary>The "What can I help with?" choices on the form, value → label used in the email.</summary>
    public static readonly IReadOnlyDictionary<string, string> Topics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["custom-software"] = "Custom app or tool",
        ["business-setup"] = "Business setup (website, domain)",
        ["automation"] = "Workflow automation",
        ["consulting"] = "Consulting",
        ["products"] = "One of the products",
        ["other"] = "Something else",
    };

    public const int MaxName = 100;
    public const int MaxEmail = 254;
    public const int MaxCompany = 120;
    public const int MinMessage = 10;
    public const int MaxMessage = 5000;
    public const int MaxPerSenderPerHour = 5;

    /// <summary>A person can't read the form and write a message faster than this.</summary>
    public static readonly TimeSpan MinFillTime = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromHours(1);

    private readonly IContactMailer _mailer;
    private readonly TimeProvider _clock;
    private readonly ILogger<ContactRequestHandler> _logger;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _recent = new();

    public ContactRequestHandler(IContactMailer mailer, ILoggerFactory loggerFactory, TimeProvider? clock = null)
    {
        _mailer = mailer;
        _clock = clock ?? TimeProvider.System;
        _logger = loggerFactory.CreateLogger<ContactRequestHandler>();
    }

    public async Task<ContactResult> HandleAsync(ContactSubmission submission, string senderKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(submission.Website))
        {
            _logger.LogInformation("Contact form: discarded a submission that filled the honeypot.");
            return new ContactResult(ContactOutcome.Discarded);
        }
        if (submission.ElapsedMs is { } ms && ms < MinFillTime.TotalMilliseconds)
        {
            _logger.LogInformation("Contact form: discarded a submission sent {Ms} ms after the page loaded.", ms);
            return new ContactResult(ContactOutcome.Discarded);
        }

        var validation = Validate(submission, out var message);
        if (validation is not null)
            return new ContactResult(ContactOutcome.Invalid, validation);

        if (!TryTakeSlot(senderKey))
        {
            _logger.LogWarning("Contact form: throttled a sender after {Max} messages in an hour.", MaxPerSenderPerHour);
            return new ContactResult(ContactOutcome.Throttled, "You've sent several messages in the last hour. Please email hello@leecrowesoftware.com instead.");
        }

        var sent = await _mailer.SendContactMessageAsync(message!, cancellationToken);
        return sent
            ? new ContactResult(ContactOutcome.Sent)
            : new ContactResult(ContactOutcome.Failed, "The message couldn't be sent just now. Please email hello@leecrowesoftware.com instead.");
    }

    private static string? Validate(ContactSubmission s, out ContactMessage? message)
    {
        message = null;
        var name = Clean(s.Name);
        var email = Clean(s.Email);
        var company = Clean(s.Company);
        var topic = Clean(s.Topic);
        var body = (s.Message ?? string.Empty).Trim();

        if (name.Length == 0) return "Please enter your name.";
        if (name.Length > MaxName) return $"Your name can be up to {MaxName} characters.";
        // Name and email end up in the email's headers (subject, reply-to). Line breaks there are
        // how header injection works, so they are refused rather than stripped.
        if (HasLineBreak(s.Name) || HasLineBreak(s.Email)) return "Please remove line breaks from your name and email.";
        if (email.Length == 0) return "Please enter your email address.";
        if (email.Length > MaxEmail || !IsEmail(email)) return "That email address doesn't look right.";
        if (company.Length > MaxCompany) return $"The company name can be up to {MaxCompany} characters.";
        if (!Topics.ContainsKey(topic)) topic = "other";
        if (body.Length < MinMessage) return "Please tell me a little more in your message.";
        if (body.Length > MaxMessage) return $"Messages can be up to {MaxMessage} characters.";

        message = new ContactMessage(name, email, company.Length == 0 ? null : company, Topics[topic], body);
        return null;
    }

    private bool TryTakeSlot(string senderKey)
    {
        var now = _clock.GetUtcNow();
        var queue = _recent.GetOrAdd(senderKey, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > ThrottleWindow)
                queue.Dequeue();
            if (queue.Count >= MaxPerSenderPerHour)
                return false;
            queue.Enqueue(now);
        }

        // Keep the map from growing without bound on a long-lived instance.
        if (_recent.Count > 10_000)
        {
            foreach (var (key, q) in _recent)
            {
                lock (q)
                {
                    if (q.Count == 0 || now - q.Peek() > ThrottleWindow)
                        _recent.TryRemove(key, out _);
                }
            }
        }
        return true;
    }

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    private static bool HasLineBreak(string? value) =>
        value is not null && value.Trim().IndexOfAny(['\r', '\n']) >= 0;

    private static bool IsEmail(string value)
    {
        if (!MailAddress.TryCreate(value, out var parsed)) return false;
        // MailAddress accepts "Name <a@b>" and bare "a@b"; only the bare form is wanted, and the
        // domain needs a dot so "a@localhost" style typos are caught.
        return string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase)
            && parsed.Host.Contains('.');
    }
}
