using AzureAIProxy.Services;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Integration;

/// <summary>
/// Verifies that when catalog entries were encrypted with a different key than the
/// current EncryptionKey, CatalogService logs a warning and returns null (404)
/// instead of throwing CryptographicException (500).
/// </summary>
public class CryptoMismatchTests
{
    private const string OriginalKey = "original-encryption-key-used-to-encrypt";
    private const string DifferentKey = "different-key-simulating-rotation";

    private static async Task<ITableStorageService> RequireAzuriteAsync()
    {
        var client = await AzuriteHelper.TryCreateLocalAzuriteClientAsync();
        Skip.If(client is null, "Azurite is not available — skipping integration test");
        return new TableStorageService(client!);
    }

    [SkippableFact]
    public async Task GetCatalogItem_MismatchedEncryptionKey_ReturnsNull_InsteadOfThrowing()
    {
        var tableStorage = await RequireAzuriteAsync();
        var originalEncryption = new EncryptionService(OriginalKey);
        var differentEncryption = new EncryptionService(DifferentKey);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // Seed event and catalog using the ORIGINAL key
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await eventsTable.UpsertEntityAsync(new EventEntity
        {
            PartitionKey = eventId,
            RowKey = eventId,
            OwnerId = "owner",
            EventCode = "CRYPTO-TEST",
            EventMarkdown = "# Test",
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddHours(1),
            TimeZoneOffset = 0,
            TimeZoneLabel = "UTC",
            OrganizerName = "Org",
            OrganizerEmail = "org@example.com",
            MaxTokenCap = 500,
            DailyRequestCap = 1000,
            Active = true,
            CatalogIds = catalogId
        }, Azure.Data.Tables.TableUpdateMode.Replace);

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await catalogTable.UpsertEntityAsync(new CatalogEntity
        {
            PartitionKey = catalogId,
            RowKey = catalogId,
            OwnerId = "owner",
            DeploymentName = "gpt-4o",
            Active = true,
            ModelType = "foundry-model",
            Location = "eastus",
            FriendlyName = "gpt-4o",
            EncryptedEndpointUrl = originalEncryption.Encrypt("https://endpoint.example.com"),
            EncryptedEndpointKey = originalEncryption.Encrypt("secret-key"),
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        }, Azure.Data.Tables.TableUpdateMode.Replace);

        // Construct CatalogService with the DIFFERENT key — simulating key rotation
        var catalogService = new CatalogService(
            tableStorage,
            differentEncryption,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheService(),
            NullLogger<CatalogService>.Instance);

        // Should return null (graceful degradation), NOT throw CryptographicException
        var (deployment, _) = await catalogService.GetCatalogItemAsync(eventId, "gpt-4o");

        Assert.Null(deployment);
    }

    [SkippableFact]
    public async Task GetEventFoundryAgent_MismatchedKey_ReturnsNull()
    {
        var tableStorage = await RequireAzuriteAsync();
        var originalEncryption = new EncryptionService(OriginalKey);
        var differentEncryption = new EncryptionService(DifferentKey);

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await eventsTable.UpsertEntityAsync(new EventEntity
        {
            PartitionKey = eventId,
            RowKey = eventId,
            OwnerId = "owner",
            EventCode = "CRYPTO-AGENT",
            EventMarkdown = "# Test",
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddHours(1),
            TimeZoneOffset = 0,
            TimeZoneLabel = "UTC",
            OrganizerName = "Org",
            OrganizerEmail = "org@example.com",
            MaxTokenCap = 500,
            DailyRequestCap = 1000,
            Active = true,
            CatalogIds = catalogId
        }, Azure.Data.Tables.TableUpdateMode.Replace);

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await catalogTable.UpsertEntityAsync(new CatalogEntity
        {
            PartitionKey = catalogId,
            RowKey = catalogId,
            OwnerId = "owner",
            DeploymentName = "my-agent",
            Active = true,
            ModelType = "foundry-agent",
            Location = "eastus",
            FriendlyName = "my-agent",
            EncryptedEndpointUrl = originalEncryption.Encrypt("https://agent.example.com"),
            EncryptedEndpointKey = originalEncryption.Encrypt("agent-key"),
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        }, Azure.Data.Tables.TableUpdateMode.Replace);

        var catalogService = new CatalogService(
            tableStorage,
            differentEncryption,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheService(),
            NullLogger<CatalogService>.Instance);

        var deployment = await catalogService.GetEventFoundryAgentAsync(eventId);

        Assert.Null(deployment);
    }
}
