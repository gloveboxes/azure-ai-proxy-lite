using Azure.Data.Tables;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Integration;

/// <summary>
/// Verifies that two events owned by different organisers have full data isolation:
/// API keys, attendee registrations, and catalog/deployment access do not leak
/// across event boundaries.
/// </summary>
public class EventIsolationTests
{
    private const string EncryptionKey = "test-encryption-key-for-isolation";

    private static async Task<(TableServiceClient client, ITableStorageService storage)> RequireAzuriteAsync()
    {
        var client = await AzuriteHelper.TryCreateLocalAzuriteClientAsync();
        Skip.If(client is null, "Azurite is not available — skipping integration test");
        var storage = new TableStorageService(client!);
        return (client!, storage);
    }

    [SkippableFact]
    public async Task Keys_From_EventA_Cannot_Authorise_For_EventB()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();

        var eventA = $"evt-a-{Guid.NewGuid():N}";
        var eventB = $"evt-b-{Guid.NewGuid():N}";

        // Seed two active events with different owners
        await SeedEventAsync(tableStorage, eventA, ownerId: "owner-alice");
        await SeedEventAsync(tableStorage, eventB, ownerId: "owner-bob");

        // Register attendees — each gets a key scoped to their event
        var attendeeService = new AttendeeService(tableStorage);
        var keyA = await attendeeService.AddAttendeeAsync("user-1", eventA);
        var keyB = await attendeeService.AddAttendeeAsync("user-2", eventB);

        // Keys must be different
        Assert.NotEqual(keyA, keyB);

        // Authorize each key
        var authorizeService = CreateAuthorizeService(tableStorage);
        var ctxA = await authorizeService.IsUserAuthorizedAsync(keyA);
        var ctxB = await authorizeService.IsUserAuthorizedAsync(keyB);

        Assert.NotNull(ctxA);
        Assert.NotNull(ctxB);

        // Key A resolves to Event A only
        Assert.Equal(eventA, ctxA!.EventId);
        Assert.NotEqual(eventB, ctxA.EventId);

        // Key B resolves to Event B only
        Assert.Equal(eventB, ctxB!.EventId);
        Assert.NotEqual(eventA, ctxB.EventId);
    }

    [SkippableFact]
    public async Task Same_User_Different_Events_Keys_Resolve_To_Correct_Event()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();

        var eventA = $"evt-a-{Guid.NewGuid():N}";
        var eventB = $"evt-b-{Guid.NewGuid():N}";

        await SeedEventAsync(tableStorage, eventA, ownerId: "owner-alice");
        await SeedEventAsync(tableStorage, eventB, ownerId: "owner-bob");

        // Same user registers for both events
        var attendeeService = new AttendeeService(tableStorage);
        var keyA = await attendeeService.AddAttendeeAsync("shared-user", eventA);
        var keyB = await attendeeService.AddAttendeeAsync("shared-user", eventB);

        Assert.NotEqual(keyA, keyB);

        var authorizeService = CreateAuthorizeService(tableStorage);

        var ctxA = await authorizeService.IsUserAuthorizedAsync(keyA);
        var ctxB = await authorizeService.IsUserAuthorizedAsync(keyB);

        Assert.Equal(eventA, ctxA!.EventId);
        Assert.Equal(eventB, ctxB!.EventId);
    }

    [SkippableFact]
    public async Task Catalog_Deployments_Are_Isolated_Between_Events()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var encryption = new EncryptionService(EncryptionKey);

        var eventA = $"evt-a-{Guid.NewGuid():N}";
        var eventB = $"evt-b-{Guid.NewGuid():N}";
        var catalogA = Guid.NewGuid().ToString();
        var catalogB = Guid.NewGuid().ToString();

        // Seed events — each with its own catalog
        await SeedEventAsync(tableStorage, eventA, ownerId: "owner-alice", catalogIds: catalogA);
        await SeedEventAsync(tableStorage, eventB, ownerId: "owner-bob", catalogIds: catalogB);

        // Seed catalog entries — different deployment names per event
        await SeedCatalogAsync(tableStorage, encryption, catalogA, deploymentName: "gpt-4o", modelType: "foundry-model");
        await SeedCatalogAsync(tableStorage, encryption, catalogB, deploymentName: "gpt-4o-mini", modelType: "foundry-model");

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogCache = new CatalogCacheService();
        var catalogService = new CatalogService(tableStorage, encryption, memoryCache, catalogCache, NullLogger<CatalogService>.Instance);

        // Event A can see gpt-4o but NOT gpt-4o-mini
        var (deployA1, _) = await catalogService.GetCatalogItemAsync(eventA, "gpt-4o");
        Assert.NotNull(deployA1);
        Assert.Equal("gpt-4o", deployA1!.DeploymentName);

        var (deployA2, catalogA2) = await catalogService.GetCatalogItemAsync(eventA, "gpt-4o-mini");
        Assert.Null(deployA2);
        Assert.DoesNotContain(catalogA2, d => d.DeploymentName == "gpt-4o-mini");

        // Event B can see gpt-4o-mini but NOT gpt-4o
        var (deployB1, _) = await catalogService.GetCatalogItemAsync(eventB, "gpt-4o-mini");
        Assert.NotNull(deployB1);
        Assert.Equal("gpt-4o-mini", deployB1!.DeploymentName);

        var (deployB2, catalogB2) = await catalogService.GetCatalogItemAsync(eventB, "gpt-4o");
        Assert.Null(deployB2);
        Assert.DoesNotContain(catalogB2, d => d.DeploymentName == "gpt-4o");
    }

    [SkippableFact]
    public async Task Full_EndToEnd_KeyA_Cannot_Access_EventB_Deployments()
    {
        var (_, tableStorage) = await RequireAzuriteAsync();
        var encryption = new EncryptionService(EncryptionKey);

        var eventA = $"evt-a-{Guid.NewGuid():N}";
        var eventB = $"evt-b-{Guid.NewGuid():N}";
        var catalogA = Guid.NewGuid().ToString();
        var catalogB = Guid.NewGuid().ToString();

        await SeedEventAsync(tableStorage, eventA, ownerId: "owner-alice", catalogIds: catalogA);
        await SeedEventAsync(tableStorage, eventB, ownerId: "owner-bob", catalogIds: catalogB);
        await SeedCatalogAsync(tableStorage, encryption, catalogA, deploymentName: "model-a", modelType: "foundry-model");
        await SeedCatalogAsync(tableStorage, encryption, catalogB, deploymentName: "model-b", modelType: "foundry-model");

        // Register attendees
        var attendeeService = new AttendeeService(tableStorage);
        var keyA = await attendeeService.AddAttendeeAsync("user-alice", eventA);
        var keyB = await attendeeService.AddAttendeeAsync("user-bob", eventB);

        // Authorize — get event IDs from keys
        var authorizeService = CreateAuthorizeService(tableStorage);
        var ctxA = await authorizeService.IsUserAuthorizedAsync(keyA);
        var ctxB = await authorizeService.IsUserAuthorizedAsync(keyB);
        Assert.NotNull(ctxA);
        Assert.NotNull(ctxB);

        // Now use those event IDs to look up deployments
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogService = new CatalogService(tableStorage, encryption, memoryCache, new CatalogCacheService(), NullLogger<CatalogService>.Instance);

        // User A's key resolves to eventA — can access model-a, NOT model-b
        var (dA, _) = await catalogService.GetCatalogItemAsync(ctxA!.EventId, "model-a");
        Assert.NotNull(dA);
        var (dACross, _) = await catalogService.GetCatalogItemAsync(ctxA.EventId, "model-b");
        Assert.Null(dACross);

        // User B's key resolves to eventB — can access model-b, NOT model-a
        var (dB, _) = await catalogService.GetCatalogItemAsync(ctxB!.EventId, "model-b");
        Assert.NotNull(dB);
        var (dBCross, _) = await catalogService.GetCatalogItemAsync(ctxB.EventId, "model-a");
        Assert.Null(dBCross);
    }

    #region Helpers

    private static AuthorizeService CreateAuthorizeService(ITableStorageService tableStorage) =>
        new(
            tableStorage,
            new EventLookupService(tableStorage, new MemoryCache(new MemoryCacheOptions()), new EventCacheService()),
            NullLogger<AuthorizeService>.Instance);

    private static async Task SeedEventAsync(
        ITableStorageService tableStorage,
        string eventId,
        string ownerId,
        string catalogIds = "")
    {
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await eventsTable.UpsertEntityAsync(new EventEntity
        {
            PartitionKey = eventId,
            RowKey = eventId,
            OwnerId = ownerId,
            EventCode = $"CODE-{eventId[..8]}",
            EventMarkdown = "# Test Event",
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddHours(1),
            TimeZoneOffset = 0,
            TimeZoneLabel = "UTC",
            OrganizerName = $"Org-{ownerId}",
            OrganizerEmail = $"{ownerId}@example.com",
            MaxTokenCap = 500,
            DailyRequestCap = 1000,
            Active = true,
            CatalogIds = catalogIds
        }, TableUpdateMode.Replace);
    }

    private static async Task SeedCatalogAsync(
        ITableStorageService tableStorage,
        IEncryptionService encryption,
        string catalogId,
        string deploymentName,
        string modelType)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await catalogTable.UpsertEntityAsync(new CatalogEntity
        {
            PartitionKey = catalogId,
            RowKey = catalogId,
            OwnerId = "owner",
            DeploymentName = deploymentName,
            Active = true,
            ModelType = modelType,
            Location = "eastus",
            FriendlyName = deploymentName,
            EncryptedEndpointUrl = encryption.Encrypt("https://fake-endpoint.example.com"),
            EncryptedEndpointKey = encryption.Encrypt("fake-api-key"),
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        }, TableUpdateMode.Replace);
    }

    #endregion

}
