using Azure;
using AzureAIProxy.Models;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Services;

public class AttendeeService(ITableStorageService tableStorage) : IAttendeeService
{
    public async Task<string> AddAttendeeAsync(string userId, string eventId)
    {
        var attendeeTable = tableStorage.GetTableClient(TableNames.Attendees);
        var lookupTable = tableStorage.GetTableClient(TableNames.AttendeeLookup);

        // Check if attendee already exists
        try
        {
            var existing = await attendeeTable.GetEntityAsync<AttendeeEntity>(eventId, userId);
            return existing.Value.ApiKey;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            var apiKey = Guid.NewGuid().ToString();

            var attendee = new AttendeeEntity
            {
                PartitionKey = eventId,
                RowKey = userId,
                ApiKey = apiKey,
                Active = true
            };

            await attendeeTable.AddEntityAsync(attendee);

            // Get event data for denormalized lookup
            var eventsTable = tableStorage.GetTableClient(TableNames.Events);
            var evtResponse = await eventsTable.GetEntityAsync<EventEntity>(eventId, eventId);
            var evt = evtResponse.Value;

            var lookup = new AttendeeLookupEntity
            {
                PartitionKey = AttendeeLookupEntity.GetPartitionKey(apiKey),
                RowKey = apiKey,
                EventId = eventId,
                UserId = userId,
                Active = true,
                EventCode = evt.EventCode,
                OrganizerName = evt.OrganizerName,
                OrganizerEmail = evt.OrganizerEmail,
                EventImageUrl = evt.EventImageUrl,
                MaxTokenCap = evt.MaxTokenCap,
                DailyRequestCap = evt.DailyRequestCap,
                EventActive = evt.Active,
                StartTimestamp = evt.StartTimestamp,
                EndTimestamp = evt.EndTimestamp,
                TimeZoneOffset = evt.TimeZoneOffset
            };

            await lookupTable.AddEntityAsync(lookup);

            return apiKey;
        }
    }

    public async Task<AttendeeKey?> GetAttendeeKeyAsync(string userId, string eventId)
    {
        var table = tableStorage.GetTableClient(TableNames.Attendees);
        try
        {
            var response = await table.GetEntityAsync<AttendeeEntity>(eventId, userId);
            return new AttendeeKey(response.Value.ApiKey, response.Value.Active);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
