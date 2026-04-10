using Azure.Data.Tables;

namespace AzureAIProxy.Tests.Fixtures;

/// <summary>
/// Shared helper for connecting to a local Azurite table storage emulator.
/// </summary>
public static class AzuriteHelper
{
    public static async Task<TableServiceClient?> TryCreateLocalAzuriteClientAsync()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            var client = await TryConnectAsync(explicitConnectionString);
            if (client is not null)
                return client;
        }

        var defaultAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        var candidates = new[]
        {
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={defaultAccountKey};TableEndpoint=http://127.0.0.1:10102/devstoreaccount1;",
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={defaultAccountKey};TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
        };

        foreach (var connectionString in candidates)
        {
            var client = await TryConnectAsync(connectionString);
            if (client is not null)
                return client;
        }

        return null;
    }

    public static async Task<(TableServiceClient client, string connectionString)?> TryCreateLocalAzuriteClientWithConnectionStringAsync()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            var client = await TryConnectAsync(explicitConnectionString);
            if (client is not null)
                return (client, explicitConnectionString);
        }

        var defaultAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
        var candidates = new[]
        {
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={defaultAccountKey};TableEndpoint=http://127.0.0.1:10102/devstoreaccount1;",
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={defaultAccountKey};TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;"
        };

        foreach (var connectionString in candidates)
        {
            var client = await TryConnectAsync(connectionString);
            if (client is not null)
                return (client, connectionString);
        }

        return null;
    }

    private static async Task<TableServiceClient?> TryConnectAsync(string connectionString)
    {
        try
        {
            var client = new TableServiceClient(connectionString);
            await client.GetTableClient("azuritehealthcheck").CreateIfNotExistsAsync();
            return client;
        }
        catch
        {
            return null;
        }
    }
}
