using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using RequestContext = AzureAIProxy.Shared.Database.RequestContext;

namespace AzureAIProxy.Services;

public class AuthorizeService(ITableStorageService tableStorage, IEventLookupService eventLookupService, ILogger<AuthorizeService> logger) : IAuthorizeService
{
    public async Task<RequestContext?> IsUserAuthorizedAsync(string apiKey)
    {
        var lookupTable = tableStorage.GetTableClient(TableNames.AttendeeLookup);
        AttendeeLookupEntity? lookup;
        try
        {
            var response = await lookupTable.GetEntityAsync<AttendeeLookupEntity>(
                AttendeeLookupEntity.GetPartitionKey(apiKey), apiKey);
            lookup = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            lookup = await HandleSharedCodeRequestAsync(apiKey);
            if (lookup is null)
            {
                logger.LogWarning("API key not found in attendee lookup table and does not match shared-code format.");
                return null;
            }
        }

        // Fetch event data via cached lookup
        var evt = await eventLookupService.GetEventAsync(lookup.EventId);
        if (evt is null)
        {
            logger.LogWarning("Event {EventId} not found for attendee.", lookup.EventId);
            return null;
        }

        if (!lookup.Active || !evt.Active)
        {
            logger.LogWarning(
                "Authentication denied: attendee active={AttendeeActive}, event active={EventActive}, eventId={EventId}",
                lookup.Active, evt.Active, lookup.EventId);
            return null;
        }

        // Check time window using event data
        var currentUtc = DateTime.UtcNow;
        var adjustedTime = currentUtc.AddMinutes(evt.TimeZoneOffset);
        if (adjustedTime < evt.StartTimestamp || adjustedTime > evt.EndTimestamp)
        {
            logger.LogWarning(
                "Authentication denied: event time window expired or not yet started. adjustedTime={AdjustedTime}, start={Start}, end={End}, eventId={EventId}",
                adjustedTime, evt.StartTimestamp, evt.EndTimestamp, lookup.EventId);
            return null;
        }

        return new RequestContext
        {
            ApiKey = apiKey,
            UserId = lookup.UserId,
            EventId = lookup.EventId,
            EventCode = evt.EventCode,
            OrganizerName = evt.OrganizerName,
            OrganizerEmail = evt.OrganizerEmail,
            MaxTokenCap = evt.MaxTokenCap,
            DailyRequestCap = evt.DailyRequestCap,
            RateLimitExceed = false,
            IsAuthorized = true
        };
    }

    private async Task<AttendeeLookupEntity?> HandleSharedCodeRequestAsync(string apiKey)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(apiKey, @"^[a-zA-Z0-9-]{9}@{1}[a-zA-Z0-9]{5,}/.{8,}$"))
            return null;

        var eventId = System.Text.RegularExpressions.Regex.Match(apiKey, @"([a-zA-Z0-9-]+)").Groups[1].Value;
        var sharedCode = System.Text.RegularExpressions.Regex.Match(apiKey, @"@([a-zA-Z0-9]+)").Groups[1].Value;

        // Fetch event via cached lookup
        var evt = await eventLookupService.GetEventAsync(eventId);
        if (evt is null || evt.EventSharedCode != sharedCode)
            return null;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        var hashString = Convert.ToHexStringLower(hashBytes);
        var generatedApiKey =
            $"{hashString[..8]}-{hashString[9..13]}-{hashString[19..23]}-{hashString[29..33]}-{hashString[39..51]}";

        var attendeeTable = tableStorage.GetTableClient(TableNames.Attendees);
        var lookupTable = tableStorage.GetTableClient(TableNames.AttendeeLookup);
        var userId = $"shared:{hashString}";

        try
        {
            await attendeeTable.AddEntityAsync(new AttendeeEntity
            {
                PartitionKey = eventId,
                RowKey = userId,
                ApiKey = generatedApiKey,
                Active = true
            });
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { }

        var lookup = new AttendeeLookupEntity
        {
            PartitionKey = AttendeeLookupEntity.GetPartitionKey(generatedApiKey),
            RowKey = generatedApiKey,
            EventId = eventId,
            UserId = userId,
            Active = true
        };

        try { await lookupTable.AddEntityAsync(lookup); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }

        return lookup;
    }

    public Task<string?> GetRequestContextFromJwtAsync(string jwt)
    {
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(jwt));
        var principal = JsonSerializer.Deserialize<JsonElement>(decoded);

        if (principal.TryGetProperty("userId", out var userIdElement)
            && userIdElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(userIdElement.GetString()))
        {
            return Task.FromResult(userIdElement.GetString());
        }
        return Task.FromResult((string?)null);
    }
}
