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

            var lookup = new AttendeeLookupEntity
            {
                PartitionKey = AttendeeLookupEntity.GetPartitionKey(apiKey),
                RowKey = apiKey,
                EventId = eventId,
                UserId = userId,
                Active = true
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
