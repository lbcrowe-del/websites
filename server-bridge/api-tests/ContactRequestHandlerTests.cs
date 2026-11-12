using Microsoft.Extensions.Logging.Abstractions;
using ServerBridge.LicensingApi.Services;
using Xunit;

namespace ServerBridge.LicensingApi.Tests;

/// <summary>
/// The leecrowesoftware.com contact form: what gets emailed, what is refused, and that bots and
/// floods are filtered. No storage is involved, so these need no emulator.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ContactRequestHandlerTests
{
    private sealed class RecordingMailer : IContactMailer
    {
        public List<ContactMessage> Sent { get; } = [];
        public bool Accept { get; set; } = true;

        public Task<bool> SendContactMessageAsync(ContactMessage message, CancellationToken cancellationToken)
        {
            if (Accept) Sent.Add(message);
            return Task.FromResult(Accept);
        }
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ContactSubmission Valid(
        string? name = "Pat Example", string? email = "pat@example.com", string? company = "Example LLC",
        string? topic = "automation", string? message = "I'd like to automate our invoicing.",
        string? website = null, long? elapsedMs = 20_000)
        => new(name, email, company, topic, message, website, elapsedMs);

    private static (ContactRequestHandler Handler, RecordingMailer Mailer, ManualClock Clock) Create()
    {
        var mailer = new RecordingMailer();
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        return (new ContactRequestHandler(mailer, NullLoggerFactory.Instance, clock), mailer, clock);
    }

    [Fact]
    public async Task ValidSubmission_IsEmailedWithTopicLabel()
    {
        var (handler, mailer, _) = Create();

        var result = await handler.HandleAsync(Valid(name: "  Pat Example "), "ip1", default);

        Assert.Equal(ContactOutcome.Sent, result.Outcome);
        var sent = Assert.Single(mailer.Sent);
        Assert.Equal("Pat Example", sent.Name);
        Assert.Equal("pat@example.com", sent.Email);
        Assert.Equal("Example LLC", sent.Company);
        Assert.Equal("Workflow automation", sent.Topic);
    }

    [Fact]
    public async Task UnknownOrMissingTopic_FallsBackToOther()
    {
        var (handler, mailer, _) = Create();

        await handler.HandleAsync(Valid(topic: "<script>"), "ip1", default);
        await handler.HandleAsync(Valid(topic: null), "ip2", default);

        Assert.All(mailer.Sent, m => Assert.Equal("Something else", m.Topic));
    }

    [Fact]
    public async Task BlankCompany_IsNull()
    {
        var (handler, mailer, _) = Create();
        await handler.HandleAsync(Valid(company: "   "), "ip1", default);
        Assert.Null(Assert.Single(mailer.Sent).Company);
    }

    [Fact]
    public async Task HoneypotFilled_IsDiscardedQuietly()
    {
        var (handler, mailer, _) = Create();

        var result = await handler.HandleAsync(Valid(website: "http://spam.example"), "ip1", default);

        Assert.Equal(ContactOutcome.Discarded, result.Outcome);
        Assert.Empty(mailer.Sent);
    }

    [Fact]
    public async Task SentTooFast_IsDiscarded_ButNoElapsedValueIsAllowed()
    {
        var (handler, mailer, _) = Create();

        var tooFast = await handler.HandleAsync(Valid(elapsedMs: 800), "ip1", default);
        var noScript = await handler.HandleAsync(Valid(elapsedMs: null), "ip2", default);

        Assert.Equal(ContactOutcome.Discarded, tooFast.Outcome);
        Assert.Equal(ContactOutcome.Sent, noScript.Outcome);
        Assert.Single(mailer.Sent);
    }

    [Theory]
    [InlineData("", "pat@example.com", "A long enough message.")]
    [InlineData("Pat", "", "A long enough message.")]
    [InlineData("Pat", "not-an-email", "A long enough message.")]
    [InlineData("Pat", "pat@localhost", "A long enough message.")]
    [InlineData("Pat", "Pat <pat@example.com>", "A long enough message.")]
    [InlineData("Pat", "pat@example.com", "short")]
    [InlineData("Pat\r\nBcc: victim@example.com", "pat@example.com", "A long enough message.")]
    [InlineData("Pat", "pat@example.com\nBcc: victim@example.com", "A long enough message.")]
    public async Task InvalidFields_AreRefusedWithAReason(string name, string email, string message)
    {
        var (handler, mailer, _) = Create();

        var result = await handler.HandleAsync(Valid(name: name, email: email, message: message), "ip1", default);

        Assert.Equal(ContactOutcome.Invalid, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(mailer.Sent);
    }

    [Fact]
    public async Task OverlongMessage_IsRefused()
    {
        var (handler, _, _) = Create();
        var result = await handler.HandleAsync(Valid(message: new string('a', ContactRequestHandler.MaxMessage + 1)), "ip1", default);
        Assert.Equal(ContactOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task SixthMessageInAnHour_IsThrottled_ThenAllowedAfterTheWindow()
    {
        var (handler, mailer, clock) = Create();

        for (var i = 0; i < ContactRequestHandler.MaxPerSenderPerHour; i++)
        {
            Assert.Equal(ContactOutcome.Sent, (await handler.HandleAsync(Valid(), "same-ip", default)).Outcome);
            clock.Now = clock.Now.AddMinutes(1);
        }

        Assert.Equal(ContactOutcome.Throttled, (await handler.HandleAsync(Valid(), "same-ip", default)).Outcome);
        Assert.Equal(ContactOutcome.Sent, (await handler.HandleAsync(Valid(), "other-ip", default)).Outcome);

        clock.Now = clock.Now.AddHours(1);
        Assert.Equal(ContactOutcome.Sent, (await handler.HandleAsync(Valid(), "same-ip", default)).Outcome);
        Assert.Equal(ContactRequestHandler.MaxPerSenderPerHour + 2, mailer.Sent.Count);
    }

    [Fact]
    public async Task InvalidSubmissions_DoNotUseUpTheThrottle()
    {
        var (handler, _, _) = Create();

        for (var i = 0; i < 10; i++)
            await handler.HandleAsync(Valid(email: "bad"), "same-ip", default);

        Assert.Equal(ContactOutcome.Sent, (await handler.HandleAsync(Valid(), "same-ip", default)).Outcome);
    }

    [Fact]
    public async Task MailerFailure_IsReportedSoTheVisitorCanEmailInstead()
    {
        var (handler, mailer, _) = Create();
        mailer.Accept = false;

        var result = await handler.HandleAsync(Valid(), "ip1", default);

        Assert.Equal(ContactOutcome.Failed, result.Outcome);
        Assert.Contains("hello@leecrowesoftware.com", result.Error);
    }
}
