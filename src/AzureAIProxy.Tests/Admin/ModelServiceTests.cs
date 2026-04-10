using Azure.Data.Tables;
using AzureAIProxy.Management.Components.ModelManagement;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;

namespace AzureAIProxy.Tests.Admin;

public class ModelServiceTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private IEncryptionService _encryption = null!;
    private readonly string OwnerId = $"model-owner-{Guid.NewGuid():N}";
    private const string EncryptionKey = "dev-encryption-key-change-in-production";

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
        if (_azurite is not null)
        {
            _tableStorage = new TableStorageService(_azurite.Value.client);
            _encryption = new EncryptionService(EncryptionKey);

            // Seed owner record (required by AddOwnerCatalogAsync)
            var ownerTable = _tableStorage.GetTableClient(TableNames.Owners);
            await ownerTable.UpsertEntityAsync(new OwnerEntity
            {
                PartitionKey = "owner",
                RowKey = OwnerId,
                Name = "Test Owner",
                Email = "owner@example.com"
            });
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ModelService CreateService(string? userId = null) =>
        new(new StubManagementAuthService(userId ?? OwnerId), _tableStorage, _encryption, new NoopCacheInvalidationService());

    private static ModelEditorModel CreateModel(string deployment = "gpt-4o", string friendly = "GPT-4o") => new()
    {
        FriendlyName = friendly,
        DeploymentName = deployment,
        EndpointUrl = "https://fake.openai.azure.com",
        EndpointKey = "fake-api-key",
        Location = "eastus",
        Active = true,
        ModelType = ModelType.Foundry_Model,
        UseManagedIdentity = false,
        UseMaxCompletionTokens = false
    };

    [SkippableFact]
    public async Task AddCatalog_ReturnsCatalogWithId()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var result = await service.AddOwnerCatalogAsync(CreateModel("add-model-1", "Add Model 1"));

        Assert.NotEqual(Guid.Empty, result.CatalogId);
        Assert.Equal("add-model-1", result.DeploymentName);
        Assert.Equal("Add Model 1", result.FriendlyName);
    }

    [SkippableFact]
    public async Task AddCatalog_DuplicateDeploymentName_Throws()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        await service.AddOwnerCatalogAsync(CreateModel("dup-deploy", "First"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddOwnerCatalogAsync(CreateModel("dup-deploy", "Second")));

        Assert.Contains("already exists", ex.Message);
    }

    [SkippableFact]
    public async Task GetCatalog_ReturnsDecryptedEndpoint()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var added = await service.AddOwnerCatalogAsync(CreateModel("decrypt-test", "Decrypt Test"));
        var fetched = await service.GetOwnerCatalogAsync(added.CatalogId);

        Assert.NotNull(fetched);
        Assert.Equal("https://fake.openai.azure.com", fetched.EndpointUrl);
        Assert.Equal("fake-api-key", fetched.EndpointKey);
    }

    [SkippableFact]
    public async Task GetCatalog_WrongOwner_ReturnsNull()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var serviceA = CreateService();
        var serviceB = CreateService("other-model-owner");

        var added = await serviceA.AddOwnerCatalogAsync(CreateModel("isolation-test", "Isolation"));

        var result = await serviceB.GetOwnerCatalogAsync(added.CatalogId);

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task UpdateCatalog_ChangesFields()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var added = await service.AddOwnerCatalogAsync(CreateModel("upd-orig", "Original"));
        var catalog = (await service.GetOwnerCatalogAsync(added.CatalogId))!;
        catalog.FriendlyName = "Updated Name";
        catalog.Active = false;

        await service.UpdateOwnerCatalogAsync(catalog);

        var fetched = await service.GetOwnerCatalogAsync(added.CatalogId);
        Assert.Equal("Updated Name", fetched!.FriendlyName);
        Assert.False(fetched.Active);
    }

    [SkippableFact]
    public async Task UpdateCatalog_DuplicateDeploymentName_Throws()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        await service.AddOwnerCatalogAsync(CreateModel("name-A", "Name A"));
        var b = await service.AddOwnerCatalogAsync(CreateModel("name-B", "Name B"));

        var catalog = (await service.GetOwnerCatalogAsync(b.CatalogId))!;
        catalog.DeploymentName = "name-A";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateOwnerCatalogAsync(catalog));
    }

    [SkippableFact]
    public async Task DeleteCatalog_RemovesCatalog()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var added = await service.AddOwnerCatalogAsync(CreateModel("del-model", "Delete Me"));
        await service.DeleteOwnerCatalogAsync(added.CatalogId);

        var fetched = await service.GetOwnerCatalogAsync(added.CatalogId);
        Assert.Null(fetched);
    }

    [SkippableFact]
    public async Task DeleteCatalog_InUseByEvent_BlocksDeletion()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();
        var eventService = new EventService(
            new StubManagementAuthService(OwnerId), _tableStorage, new NoopCacheInvalidationService());

        var catalog = await service.AddOwnerCatalogAsync(CreateModel("in-use-model", "In Use"));
        var evt = await eventService.CreateEventAsync(new()
        {
            Name = "Linked Event",
            Description = "desc",
            Start = DateTime.UtcNow.AddHours(-1),
            End = DateTime.UtcNow.AddHours(24),
            OrganizerName = "Org",
            OrganizerEmail = "org@example.com",
            MaxTokenCap = 4096,
            DailyRequestCap = 1024,
            Active = true,
            SelectedTimeZone = TimeZoneInfo.Utc
        });

        await eventService.UpdateModelsForEventAsync(evt!.EventId, [catalog.CatalogId]);

        // Try to delete — should be blocked
        await service.DeleteOwnerCatalogAsync(catalog.CatalogId);

        var fetched = await service.GetOwnerCatalogAsync(catalog.CatalogId);
        Assert.NotNull(fetched); // Still exists
    }

    [SkippableFact]
    public async Task DuplicateCatalog_CreatesUniqueNames()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var original = await service.AddOwnerCatalogAsync(CreateModel("dup-src", "Source Model"));
        await service.DuplicateOwnerCatalogAsync(original);

        var all = (await service.GetOwnerCatalogsAsync()).ToList();
        Assert.Contains(all, c => c.DeploymentName == "dup-src-copy");
        Assert.Contains(all, c => c.FriendlyName == "Source Model (Copy)");
    }

    [SkippableFact]
    public async Task DuplicateCatalog_MultipleTimesIncrementsCounter()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var original = await service.AddOwnerCatalogAsync(CreateModel("multi-dup", "Multi Dup"));
        await service.DuplicateOwnerCatalogAsync(original);
        await service.DuplicateOwnerCatalogAsync(original);

        var all = (await service.GetOwnerCatalogsAsync()).ToList();
        Assert.Contains(all, c => c.DeploymentName == "multi-dup-copy");
        Assert.Contains(all, c => c.DeploymentName == "multi-dup-copy-2");
    }

    [SkippableFact]
    public async Task AddCatalog_ManagedIdentity_EmptyEndpointKey_Succeeds()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var service = CreateService();

        var model = CreateModel("mi-model", "MI Model");
        model.UseManagedIdentity = true;
        model.EndpointKey = null;

        var result = await service.AddOwnerCatalogAsync(model);
        var fetched = await service.GetOwnerCatalogAsync(result.CatalogId);

        Assert.NotNull(fetched);
        Assert.Equal(string.Empty, fetched.EndpointKey);
        Assert.True(fetched.UseManagedIdentity);
    }
}
