using Azure;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Caching.Memory;

namespace AzureAIProxy.Services;

public class EventLookupService(ITableStorageService tableStorage, IMemoryCache memoryCache, IEventCacheService eventCache) : IEventLookupService
{
    private const string CacheKeyPrefix = "event+lookup+";

    public async Task<EventEntity?> GetEventAsync(string eventId)
    {
        var cacheKey = $"{CacheKeyPrefix}{eventId}";
        if (memoryCache.TryGetValue(cacheKey, out EventEntity? cached))
            return cached;

        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        try
        {
            var response = await eventsTable.GetEntityAsync<EventEntity>(eventId, eventId);
            var evt = response.Value;
            memoryCache.Set(cacheKey, evt, eventCache.GetCacheEntryOptions());
            return evt;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
