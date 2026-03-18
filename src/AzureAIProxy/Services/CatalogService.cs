using Azure;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Caching.Memory;

namespace AzureAIProxy.Services;

public class CatalogService(
    ITableStorageService tableStorage,
    IEncryptionService encryption,
    IMemoryCache memoryCache
) : ICatalogService
{
    const string CatalogFoundryAgentEventKey = "catalog+foundry+agent+event+key";
    const string CatalogEventDeploymentKey = "catalog+event+deployment+key";

    /// <summary>Gets catalog IDs from the event entity's inlined CatalogIds field.</summary>
    private async Task<List<string>> GetCatalogIdsForEventAsync(string eventId)
    {
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        try
        {
            var response = await eventsTable.GetEntityAsync<EventEntity>(eventId, eventId);
            var ids = response.Value.CatalogIds;
            return string.IsNullOrEmpty(ids) ? [] : [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries)];
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    private async Task<List<Deployment>> GetDecryptedEventCatalogAsync(string eventId, string deploymentName)
    {
        var cacheKey = $"{CatalogEventDeploymentKey}+{eventId}+{deploymentName}";
        if (memoryCache.TryGetValue(cacheKey, out List<Deployment>? cachedValue))
            return cachedValue!;

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var catalogIds = await GetCatalogIdsForEventAsync(eventId);

        var result = new List<Deployment>();
        foreach (var catalogId in catalogIds)
        {
            try
            {
                // Point read — PK and RK are both catalog_id
                var response = await catalogTable.GetEntityAsync<CatalogEntity>(catalogId, catalogId);
                var catalog = response.Value;
                if (catalog.Active && catalog.DeploymentName == deploymentName)
                {
                    result.Add(new Deployment
                    {
                        DeploymentName = catalog.DeploymentName,
                        EndpointUrl = encryption.Decrypt(catalog.EncryptedEndpointUrl),
                        EndpointKey = string.IsNullOrWhiteSpace(catalog.EncryptedEndpointKey) ? string.Empty : encryption.Decrypt(catalog.EncryptedEndpointKey),
                        ModelType = catalog.ModelType,
                        CatalogId = Guid.Parse(catalogId),
                        Location = catalog.Location,
                        UseManagedIdentity = catalog.UseManagedIdentity
                    });
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }
        }

        memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
        return result;
    }

    public async Task<Deployment?> GetEventFoundryAgentAsync(string eventId)
    {
        var cacheKey = $"{CatalogFoundryAgentEventKey}+{eventId}";
        if (memoryCache.TryGetValue(cacheKey, out Deployment? cachedValue))
            return cachedValue!;

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var catalogIds = await GetCatalogIdsForEventAsync(eventId);

        foreach (var catalogId in catalogIds)
        {
            try
            {
                var response = await catalogTable.GetEntityAsync<CatalogEntity>(catalogId, catalogId);
                var catalog = response.Value;
                if (catalog.Active && catalog.ModelType == ModelType.Foundry_Agent.ToStorageString())
                {
                    var result = new Deployment
                    {
                        DeploymentName = catalog.DeploymentName,
                        EndpointUrl = encryption.Decrypt(catalog.EncryptedEndpointUrl),
                        EndpointKey = string.IsNullOrWhiteSpace(catalog.EncryptedEndpointKey) ? string.Empty : encryption.Decrypt(catalog.EncryptedEndpointKey),
                        ModelType = catalog.ModelType,
                        CatalogId = Guid.Parse(catalogId),
                        Location = catalog.Location,
                        UseManagedIdentity = catalog.UseManagedIdentity
                    };
                    memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(4));
                    return result;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }
        }

        return null;
    }

    public async Task<Deployment?> GetEventMcpServerAsync(string eventId, string deploymentName)
    {
        var cacheKey = $"catalog+mcp+server+{eventId}+{deploymentName}";
        if (memoryCache.TryGetValue(cacheKey, out Deployment? cachedValue))
            return cachedValue!;

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var catalogIds = await GetCatalogIdsForEventAsync(eventId);

        foreach (var catalogId in catalogIds)
        {
            try
            {
                var response = await catalogTable.GetEntityAsync<CatalogEntity>(catalogId, catalogId);
                var catalog = response.Value;
                if (catalog.Active &&
                    catalog.ModelType == ModelType.MCP_Server.ToStorageString() &&
                    catalog.DeploymentName == deploymentName)
                {
                    var result = new Deployment
                    {
                        DeploymentName = catalog.DeploymentName,
                        EndpointUrl = encryption.Decrypt(catalog.EncryptedEndpointUrl),
                        EndpointKey = string.IsNullOrWhiteSpace(catalog.EncryptedEndpointKey) ? string.Empty : encryption.Decrypt(catalog.EncryptedEndpointKey),
                        ModelType = catalog.ModelType,
                        CatalogId = Guid.Parse(catalogId),
                        Location = catalog.Location,
                        UseManagedIdentity = catalog.UseManagedIdentity
                    };
                    memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(4));
                    return result;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }
        }

        return null;
    }

    public async Task<(Deployment? deployment, List<Deployment> eventCatalog)> GetCatalogItemAsync(
        string eventId, string deploymentName)
    {
        deploymentName = deploymentName.Trim();
        var deployments = await GetDecryptedEventCatalogAsync(eventId, deploymentName);
        if (deployments.Count == 0)
            return (null, await GetEventCatalogAsync(eventId));
        else
            return (deployments[new Random().Next(deployments.Count)], []);
    }

    public async Task<Dictionary<string, List<string>>> GetCapabilitiesAsync(string eventId)
    {
        var deployments = await GetEventCatalogAsync(eventId);
        var capabilities = new Dictionary<string, List<string>>();

        foreach (var deployment in deployments)
        {
            if (!capabilities.TryGetValue(deployment.ModelType, out var value))
            {
                value = [];
                capabilities[deployment.ModelType] = value;
            }
            value.Add(deployment.DeploymentName);
        }

        return capabilities;
    }

    public async Task<List<string>> GetAiToolkitDeploymentsAsync(string eventId)
    {
        var deployments = await GetEventCatalogAsync(eventId);
        return deployments
            .Where(d => d.ModelType == ModelType.AI_Toolkit.ToStorageString())
            .Select(d => d.DeploymentName)
            .Distinct()
            .ToList();
    }

    private async Task<List<Deployment>> GetEventCatalogAsync(string eventId)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var catalogIds = await GetCatalogIdsForEventAsync(eventId);

        var result = new List<Deployment>();
        foreach (var catalogId in catalogIds)
        {
            try
            {
                var response = await catalogTable.GetEntityAsync<CatalogEntity>(catalogId, catalogId);
                var catalog = response.Value;
                if (catalog.Active)
                {
                    result.Add(new Deployment
                    {
                        DeploymentName = catalog.DeploymentName,
                        ModelType = catalog.ModelType,
                        Location = catalog.Location
                    });
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }
        }

        return result.DistinctBy(d => d.DeploymentName).OrderBy(d => d.DeploymentName).ToList();
    }
}
