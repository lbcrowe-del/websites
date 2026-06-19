using Azure;
using Azure.Data.Tables;
using ServerBridge.LicensingApi.Models;

namespace ServerBridge.LicensingApi.Services;

public sealed class TableLicenseRepository : ILicenseRepository
{
    private const string TableName = "Licenses";
    private readonly TableClient _table;

    public TableLicenseRepository()
    {
        var connectionString = Environment.GetEnvironmentVariable("LICENSE_TABLE_CONNECTION")
            ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("LICENSE_TABLE_CONNECTION (or AzureWebJobsStorage) is not configured.");

        _table = new TableClient(connectionString, TableName);
        _table.CreateIfNotExists();
    }

    public async Task<LicenseRecord?> GetAsync(string licenseKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<LicenseRecord>("license", licenseKey, cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task UpsertAsync(LicenseRecord record, CancellationToken cancellationToken)
    {
        await _table.UpsertEntityAsync(record, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task LinkCheckoutSessionAsync(string sessionId, string licenseKey, CancellationToken cancellationToken)
    {
        var link = new CheckoutSessionLink { RowKey = sessionId, LicenseKey = licenseKey };
        await _table.UpsertEntityAsync(link, TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<string?> GetLicenseKeyForSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<CheckoutSessionLink>("session", sessionId, cancellationToken: cancellationToken);
            return response.Value.LicenseKey;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
