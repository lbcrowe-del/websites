using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using ServerBridge.LicensingApi.Models;
using ServerBridge.LicensingApi.Services;

namespace ServerBridge.LicensingApi.Functions;

public sealed class LicenseActivateFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LicenseRequestHandler _handler;

    public LicenseActivateFunction(LicenseRequestHandler handler) => _handler = handler;

    [Function("LicenseActivate")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "license/activate")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var body = await req.ReadFromJsonAsync<LicenseRequestBody>(JsonOptions, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.LicenseKey))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteAsJsonAsync(new LicenseResponseBody(false, "Free", "None", null, "License key is required."), JsonOptions, cancellationToken);
            return bad;
        }

        var result = await _handler.ActivateAsync(body.LicenseKey, body.DeviceId, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, JsonOptions, cancellationToken);
        return response;
    }
}
