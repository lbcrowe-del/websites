using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ServerBridge.LicensingApi.Models;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

public sealed class MigrationCompleteFunction
{
    private readonly LicenseRequestHandler _handler;

    public MigrationCompleteFunction(LicenseRequestHandler handler) => _handler = handler;

    [Function("MigrationComplete")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "migration/complete")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadFromJsonAsync<MigrationCompleteRequestBody>(cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.LicenseKey))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new MigrationCompleteResponseBody(false, "License key is required."), cancellationToken);
            return bad;
        }

        var result = await _handler.ReportMigrationCompleteAsync(
            body.LicenseKey,
            body.DeviceId,
            body.FilesMigratedCount,
            body.CompletedAtUtc,
            cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
