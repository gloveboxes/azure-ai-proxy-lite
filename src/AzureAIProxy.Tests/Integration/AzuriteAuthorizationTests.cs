using Azure;
using Azure.Data.Tables;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Integration;

public class AzuriteAuthorizationTests
{
    private static async Task<(TableServiceClient client, ITableStorageService storage)> RequireAzuriteAsync()
    {
        var client = await AzuriteHelper.TryCreateLocalAzuriteClientAsync();
        Skip.If(client is null, "Azurite is not available — skipping integration test");
        var storage = new TableStorageService(client!);
        return (client!, storage);
    }

    private static AuthorizeService CreateAuthorizeService(ITableStorageService tableStorage) =>
        new(
            tableStorage,
            new EventLookupService(tableStorage, new MemoryCache(new MemoryCacheOptions()), new EventCacheService()),
            NullLogger<AuthorizeService>.Instance);

    [SkippableFact]
    public async Task IsUserAuthorizedAsync_WithSeededAzuriteData_ReturnsRequestContext()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var authorizeService = CreateAuthorizeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var apiKey = $"ab-{Guid.NewGuid():N}";

        await SeedEventAsync(tableStorage, eventId, active: true, windowStart: DateTime.UtcNow.AddHours(-1), windowEnd: DateTime.UtcNow.AddHours(1));
        await SeedAttendeeLookupAsync(tableStorage, eventId, apiKey, active: true);

        var context = await authorizeService.IsUserAuthorizedAsync(apiKey);

        Assert.NotNull(context);
        Assert.Equal(apiKey, context!.ApiKey);
        Assert.Equal(eventId, context.EventId);
        Assert.True(context.IsAuthorized);
    }

    [SkippableFact]
    public async Task IsUserAuthorizedAsync_ExpiredEvent_ReturnsNull()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var authorizeService = CreateAuthorizeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var apiKey = $"cd-{Guid.NewGuid():N}";

        await SeedEventAsync(tableStorage, eventId, active: true, windowStart: DateTime.UtcNow.AddHours(-3), windowEnd: DateTime.UtcNow.AddHours(-2));
        await SeedAttendeeLookupAsync(tableStorage, eventId, apiKey, active: true);

        var context = await authorizeService.IsUserAuthorizedAsync(apiKey);

        Assert.Null(context);
    }

    [SkippableFact]
    public async Task IsUserAuthorizedAsync_InactiveAttendee_ReturnsNull()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var authorizeService = CreateAuthorizeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var apiKey = $"ef-{Guid.NewGuid():N}";

        await SeedEventAsync(tableStorage, eventId, active: true, windowStart: DateTime.UtcNow.AddHours(-1), windowEnd: DateTime.UtcNow.AddHours(1));
        await SeedAttendeeLookupAsync(tableStorage, eventId, apiKey, active: false);

        var context = await authorizeService.IsUserAuthorizedAsync(apiKey);

        Assert.Null(context);
    }

    [SkippableFact]
    public async Task IsUserAuthorizedAsync_InactiveEvent_ReturnsNull()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var authorizeService = CreateAuthorizeService(tableStorage);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var apiKey = $"gh-{Guid.NewGuid():N}";

        await SeedEventAsync(tableStorage, eventId, active: false, windowStart: DateTime.UtcNow.AddHours(-1), windowEnd: DateTime.UtcNow.AddHours(1));
        await SeedAttendeeLookupAsync(tableStorage, eventId, apiKey, active: true);

        var context = await authorizeService.IsUserAuthorizedAsync(apiKey);

        Assert.Null(context);
    }

    [SkippableFact]
    public async Task IsUserAuthorizedAsync_UnknownApiKey_ReturnsNull()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var authorizeService = CreateAuthorizeService(tableStorage);

        var context = await authorizeService.IsUserAuthorizedAsync($"zz-{Guid.NewGuid():N}");

        Assert.Null(context);
    }

    private static async Task SeedEventAsync(
        ITableStorageService tableStorage,
        string eventId,
        bool active,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await eventsTable.UpsertEntityAsync(new EventEntity
        {
            PartitionKey = eventId,
            RowKey = eventId,
            OwnerId = "owner-1",
            EventCode = "EVENT-CODE",
            EventSharedCode = "SHARED",
            EventMarkdown = "# Event",
            StartTimestamp = windowStart,
            EndTimestamp = windowEnd,
            TimeZoneOffset = 0,
            TimeZoneLabel = "UTC",
            OrganizerName = "Organizer",
            OrganizerEmail = "organizer@example.com",
            MaxTokenCap = 500,
            DailyRequestCap = 1000,
            Active = active,
            CatalogIds = string.Empty
        }, TableUpdateMode.Replace);
    }

    private static async Task SeedAttendeeLookupAsync(
        ITableStorageService tableStorage,
        string eventId,
        string apiKey,
        bool active)
    {
        var lookupTable = tableStorage.GetTableClient(TableNames.AttendeeLookup);
        await lookupTable.UpsertEntityAsync(new AttendeeLookupEntity
        {
            PartitionKey = AttendeeLookupEntity.GetPartitionKey(apiKey),
            RowKey = apiKey,
            EventId = eventId,
            UserId = $"user-{Guid.NewGuid():N}",
            Active = active
        }, TableUpdateMode.Replace);
    }

}
