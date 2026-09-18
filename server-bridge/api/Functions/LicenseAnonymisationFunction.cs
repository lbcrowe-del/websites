using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

/// <summary>
/// Nightly retention job: erases refunded customers' contact details 120 days after deactivation.
/// The work is in <see cref="LicenseAnonymisationService"/> so it can be tested against Azurite
/// without the Functions host.
///
/// Deployed only to serverbridge-licensing-fc (deploy-licensing-api.yml targets that app alone),
/// so it runs once a night even though the old serverbridge-licensing app is still serving the
/// origin hostname off the same storage account. Were it ever deployed to both, the pass is
/// idempotent and the second one would find nothing to do — but disable it on one of them with the
/// app setting AzureWebJobs.LicenseAnonymisation.Disabled = true rather than relying on that.
/// </summary>
public sealed class LicenseAnonymisationFunction
{
    private readonly LicenseAnonymisationService _service;
    private readonly ILogger<LicenseAnonymisationFunction> _logger;

    public LicenseAnonymisationFunction(LicenseAnonymisationService service, ILoggerFactory loggerFactory)
    {
        _service = service;
        _logger = loggerFactory.CreateLogger<LicenseAnonymisationFunction>();
    }

    // 03:20 UTC daily — off the hour, so it doesn't share a minute with every other cron on the
    // platform. RunOnStartup is deliberately NOT set: a restart loop must not turn into a run loop.
    [Function("LicenseAnonymisation")]
    public async Task RunAsync([TimerTrigger("0 20 3 * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Licence anonymisation pass starting (next scheduled: {Next}).", timer.ScheduleStatus?.Next);
        await _service.RunAsync(DateTimeOffset.UtcNow, cancellationToken);
    }
}
