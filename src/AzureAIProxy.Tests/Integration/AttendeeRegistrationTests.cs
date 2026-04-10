using Azure.Data.Tables;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Integration;

public class AttendeeRegistrationTests
{
    private static async Task<(TableServiceClient client, ITableStorageService storage)> RequireAzuriteAsync()
    {
        var client = await AzuriteHelper.TryCreateLocalAzuriteClientAsync();
        Skip.If(client is null, "Azurite is not available — skipping integration test");
        var storage = new TableStorageService(client!);
        return (client!, storage);
    }

    [SkippableFact]
    public async Task AddAttendeeAsync_NewAttendee_CreatesAttendeeAndLookupEntities()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var service = new AttendeeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}";

        var apiKey = await service.AddAttendeeAsync(userId, eventId);

        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        Assert.True(Guid.TryParse(apiKey, out _));

        // Verify the AttendeeEntity was created
        var attendeeTable = tableStorage.GetTableClient(TableNames.Attendees);
        var attendee = await attendeeTable.GetEntityAsync<AttendeeEntity>(eventId, userId);
        Assert.Equal(apiKey, attendee.Value.ApiKey);
        Assert.True(attendee.Value.Active);

        // Verify the AttendeeLookupEntity was created
        var lookupTable = tableStorage.GetTableClient(TableNames.AttendeeLookup);
        var lookup = await lookupTable.GetEntityAsync<AttendeeLookupEntity>(
            AttendeeLookupEntity.GetPartitionKey(apiKey), apiKey);
        Assert.Equal(eventId, lookup.Value.EventId);
        Assert.Equal(userId, lookup.Value.UserId);
        Assert.True(lookup.Value.Active);
    }

    [SkippableFact]
    public async Task AddAttendeeAsync_ExistingAttendee_ReturnsSameKey()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var service = new AttendeeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}";

        var firstKey = await service.AddAttendeeAsync(userId, eventId);
        var secondKey = await service.AddAttendeeAsync(userId, eventId);

        Assert.Equal(firstKey, secondKey);
    }

    [SkippableFact]
    public async Task AddAttendeeAsync_SameUserDifferentEvents_GetsDifferentKeys()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var service = new AttendeeService(tableStorage);

        var userId = $"user-{Guid.NewGuid():N}";
        var eventId1 = $"evt-{Guid.NewGuid():N}";
        var eventId2 = $"evt-{Guid.NewGuid():N}";

        var key1 = await service.AddAttendeeAsync(userId, eventId1);
        var key2 = await service.AddAttendeeAsync(userId, eventId2);

        Assert.NotEqual(key1, key2);
    }

    [SkippableFact]
    public async Task GetAttendeeKeyAsync_ExistingAttendee_ReturnsKeyAndActive()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var service = new AttendeeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}";

        var apiKey = await service.AddAttendeeAsync(userId, eventId);

        var result = await service.GetAttendeeKeyAsync(userId, eventId);

        Assert.NotNull(result);
        Assert.Equal(apiKey, result!.ApiKey);
        Assert.True(result.Active);
    }

    [SkippableFact]
    public async Task GetAttendeeKeyAsync_NonExistentAttendee_ReturnsNull()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var service = new AttendeeService(tableStorage);

        var result = await service.GetAttendeeKeyAsync(
            $"user-{Guid.NewGuid():N}",
            $"evt-{Guid.NewGuid():N}");

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task RegisteredKey_CanAuthorize_WhenEventIsActive()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var attendeeService = new AttendeeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var userId = $"user-{Guid.NewGuid():N}";

        // Seed an active event with a valid time window
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await eventsTable.UpsertEntityAsync(new EventEntity
        {
            PartitionKey = eventId,
            RowKey = eventId,
            OwnerId = "owner-1",
            EventCode = "REG-TEST",
            EventMarkdown = "# Test",
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddHours(1),
            TimeZoneOffset = 0,
            TimeZoneLabel = "UTC",
            OrganizerName = "Test Org",
            OrganizerEmail = "org@example.com",
            MaxTokenCap = 500,
            DailyRequestCap = 1000,
            Active = true,
            CatalogIds = string.Empty
        }, Azure.Data.Tables.TableUpdateMode.Replace);

        // Register an attendee — this creates the lookup entity
        var apiKey = await attendeeService.AddAttendeeAsync(userId, eventId);

        // Now verify the issued key works through the full authorize flow
        var eventLookup = new EventLookupService(
            tableStorage,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            new EventCacheService());

        var authorizeService = new AuthorizeService(
            tableStorage,
            eventLookup,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthorizeService>.Instance);

        var context = await authorizeService.IsUserAuthorizedAsync(apiKey);

        Assert.NotNull(context);
        Assert.Equal(apiKey, context!.ApiKey);
        Assert.Equal(eventId, context.EventId);
        Assert.True(context.IsAuthorized);
    }

}
