using Azure;
using AzureAIProxy.Models;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Caching.Memory;

namespace AzureAIProxy.Services;

public class EventService(ITableStorageService tableStorage, IMemoryCache memoryCache) : IEventService
{
    public async Task<EventRegistration?> GetEventRegistrationInfoAsync(string eventId)
    {
        if (memoryCache.TryGetValue(eventId, out EventRegistration? cachedContext))
            return cachedContext;

        var table = tableStorage.GetTableClient(TableNames.Events);

        EventEntity? evt;
        try
        {
            var response = await table.GetEntityAsync<EventEntity>(eventId, eventId);
            evt = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }

        var result = new EventRegistration
        {
            EventId = evt.PartitionKey,
            EventCode = evt.EventCode,
            EventImageUrl = evt.EventImageUrl,
            OrganizerName = evt.OrganizerName,
            OrganizerEmail = evt.OrganizerEmail,
            EventMarkdown = evt.EventMarkdown,
            StartTimestamp = evt.StartTimestamp,
            EndTimestamp = evt.EndTimestamp,
            TimeZoneOffset = evt.TimeZoneOffset,
            TimeZoneLabel = evt.TimeZoneLabel
        };

        memoryCache.Set(eventId, result, TimeSpan.FromMinutes(1));
        return result;
    }
}
