using Azure;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Caching.Memory;

namespace AzureAIProxy.Services;

public class FoundryAgentService(ITableStorageService tableStorage, IMemoryCache memoryCache) : IFoundryAgentService
{
    const string CachePrefix = "foundry+agent+object";

    public async Task AddObjectAsync(string apiKey, string objectId, string objectType)
    {
        if (string.IsNullOrEmpty(objectId)) return;

        var table = tableStorage.GetTableClient(TableNames.FoundryAgents);
        var entity = new FoundryAgentObjectEntity
        {
            PartitionKey = apiKey,
            RowKey = objectId,
            ObjectType = objectType
        };

        try { await table.AddEntityAsync(entity); }
        catch (RequestFailedException ex) when (ex.Status == 409) { /* already exists */ }

        var cacheKey = $"{CachePrefix}+{objectId}+{apiKey}";
        memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
    }

    public async Task DeleteObjectAsync(string apiKey, string objectId)
    {
        if (string.IsNullOrEmpty(objectId)) return;

        var table = tableStorage.GetTableClient(TableNames.FoundryAgents);
        try { await table.DeleteEntityAsync(apiKey, objectId); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var cacheKey = $"{CachePrefix}+{objectId}+{apiKey}";
        memoryCache.Remove(cacheKey);
    }

    public async Task<bool> ValidateObjectAsync(string apiKey, string objectId)
    {
        var cacheKey = $"{CachePrefix}+{objectId}+{apiKey}";
        if (memoryCache.TryGetValue(cacheKey, out _))
            return true;

        var table = tableStorage.GetTableClient(TableNames.FoundryAgents);
        try
        {
            await table.GetEntityAsync<FoundryAgentObjectEntity>(apiKey, objectId);
            memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
