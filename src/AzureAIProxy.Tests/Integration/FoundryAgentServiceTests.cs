using Azure.Data.Tables;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Tests.Integration;

/// <summary>
/// Tests FoundryAgentService CRUD operations (AddObject, ValidateObject, DeleteObject)
/// against Azurite table storage.
/// </summary>
public class FoundryAgentServiceTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (TableServiceClient client, string connectionString) RequireAzurite()
    {
        Skip.If(_azurite is null, "Azurite not available");
        return _azurite!.Value;
    }

    [SkippableFact]
    public async Task AddAndValidate_ReturnsTrue()
    {
        var az = RequireAzurite();
        var service = CreateService(az);
        var apiKey = Guid.NewGuid().ToString();

        await service.AddObjectAsync(apiKey, "assistant:asst_abc123", "assistant");
        var result = await service.ValidateObjectAsync(apiKey, "assistant:asst_abc123");

        Assert.True(result);
    }

    [SkippableFact]
    public async Task Validate_UnknownObject_ReturnsFalse()
    {
        var az = RequireAzurite();
        var service = CreateService(az);

        var result = await service.ValidateObjectAsync(Guid.NewGuid().ToString(), "assistant:asst_unknown");

        Assert.False(result);
    }

    [SkippableFact]
    public async Task Delete_ThenValidate_ReturnsFalse()
    {
        var az = RequireAzurite();
        var service = CreateService(az);
        var apiKey = Guid.NewGuid().ToString();

        await service.AddObjectAsync(apiKey, "thread:thread_123", "thread");
        Assert.True(await service.ValidateObjectAsync(apiKey, "thread:thread_123"));

        await service.DeleteObjectAsync(apiKey, "thread:thread_123");
        var result = await service.ValidateObjectAsync(apiKey, "thread:thread_123");

        Assert.False(result);
    }

    [SkippableFact]
    public async Task Add_DuplicateObject_DoesNotThrow()
    {
        var az = RequireAzurite();
        var service = CreateService(az);
        var apiKey = Guid.NewGuid().ToString();

        await service.AddObjectAsync(apiKey, "file:file-abc", "file");
        await service.AddObjectAsync(apiKey, "file:file-abc", "file"); // duplicate — should not throw

        Assert.True(await service.ValidateObjectAsync(apiKey, "file:file-abc"));
    }

    [SkippableFact]
    public async Task Delete_NonexistentObject_DoesNotThrow()
    {
        var az = RequireAzurite();
        var service = CreateService(az);

        // Should not throw even though object never existed
        await service.DeleteObjectAsync(Guid.NewGuid().ToString(), "thread:thread_nonexistent");
    }

    [SkippableFact]
    public async Task Objects_IsolatedByApiKey()
    {
        var az = RequireAzurite();
        var service = CreateService(az);
        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();

        await service.AddObjectAsync(keyA, "assistant:asst_shared_id", "assistant");

        Assert.True(await service.ValidateObjectAsync(keyA, "assistant:asst_shared_id"));
        Assert.False(await service.ValidateObjectAsync(keyB, "assistant:asst_shared_id"));
    }

    [SkippableFact]
    public async Task Add_EmptyObjectId_NoOps()
    {
        var az = RequireAzurite();
        var service = CreateService(az);

        // Empty objectId should return early without throwing
        await service.AddObjectAsync(Guid.NewGuid().ToString(), "", "assistant");
    }

    private static AzureAIProxy.Services.FoundryAgentService CreateService(
        (TableServiceClient client, string connectionString) az)
    {
        var tableStorage = new AzureAIProxy.Shared.Services.TableStorageService(az.client);
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        return new AzureAIProxy.Services.FoundryAgentService(tableStorage, cache);
    }
}
