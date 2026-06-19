using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServerBridge.LicensingApi.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ILicenseRepository, TableLicenseRepository>();
        services.AddSingleton<LicenseKeyGenerator>();
        services.AddSingleton<StripeSignatureVerifier>();
        services.AddSingleton<LicenseRequestHandler>();
    })
    .Build();

host.Run();
