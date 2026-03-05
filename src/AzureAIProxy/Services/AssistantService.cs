using Azure;
using AzureAIProxy.Models;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace AzureAIProxy.Services;

public class AssistantService(ITableStorageService tableStorage, IMemoryCache memoryCache) : IAssistantService
{
    const string AssistantIdApiKey = "assistant+id+key";

    public async Task AddIdAsync(string apiKey, string responseContent)
    {
        var response = JsonSerializer.Deserialize<AssistantResponse>(responseContent);
        var id = response?.Id;
        if (id is null) return;

        var table = tableStorage.GetTableClient(TableNames.Assistants);
        var entity = new AssistantEntity { PartitionKey = apiKey, RowKey = id };

        try { await table.AddEntityAsync(entity); }
        catch (RequestFailedException ex) when (ex.Status == 409) { /* already exists */ }

        var cacheKey = $"{AssistantIdApiKey}+{id}+{apiKey}";
        memoryCache.Set(cacheKey, new Assistant { ApiKey = apiKey, Id = id }, TimeSpan.FromMinutes(10));
    }

    public async Task DeleteIdAsync(string apiKey, string responseContent)
    {
        var response = JsonSerializer.Deserialize<AssistantResponse>(responseContent);
        var id = response?.Id;
        var deleted = response?.Deleted ?? false;
        if (id is null || !deleted) return;

        var table = tableStorage.GetTableClient(TableNames.Assistants);
        try { await table.DeleteEntityAsync(apiKey, id); }
        catch (RequestFailedException ex) when (ex.Status == 404) { }

        var cacheKey = $"{AssistantIdApiKey}+{id}+{apiKey}";
        memoryCache.Remove(cacheKey);
    }

    public async Task<Assistant?> GetIdAsync(string apiKey, string id)
    {
        var cacheKey = $"{AssistantIdApiKey}+{id}+{apiKey}";
        if (memoryCache.TryGetValue(cacheKey, out Assistant? cachedValue))
            return cachedValue!;

        var table = tableStorage.GetTableClient(TableNames.Assistants);
        try
        {
            var response = await table.GetEntityAsync<AssistantEntity>(apiKey, id);
            var result = new Assistant { ApiKey = response.Value.PartitionKey, Id = response.Value.RowKey };
            memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            return result;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Assistant?> GetIdAsync(string id)
    {
        var table = tableStorage.GetTableClient(TableNames.Assistants);
        await foreach (var entity in table.QueryAsync<AssistantEntity>(e => e.RowKey == id))
        {
            return new Assistant { ApiKey = entity.PartitionKey, Id = entity.RowKey };
        }
        return null;
    }
}
