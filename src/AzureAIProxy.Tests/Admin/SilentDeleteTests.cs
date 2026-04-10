using Azure.Data.Tables;
using AzureAIProxy.Management.Components.EventManagement;
using AzureAIProxy.Management.Components.ModelManagement;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;

namespace AzureAIProxy.Tests.Admin;

/// <summary>
/// Exposes the silent-delete pattern: DeleteEventAsync / DeleteOwnerCatalogAsync
/// return Task (void), giving the caller no indication of success vs failure.
/// The UI relies on disabled buttons to prevent forbidden deletes, but a TOCTOU
/// gap exists: the state can change between page render and button click.
///
/// These tests document the current behavior with commentary on what SHOULD happen.
/// </summary>
public class SilentDeleteTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private IEncryptionService _encryption = null!;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private string OwnerId => $"del-owner-{_runId}";
    private string OtherId => $"del-other-{_runId}";
    private const string EncryptionKey = "dev-encryption-key-change-in-production";

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
        if (_azurite is null) return;

        _tableStorage = new TableStorageService(_azurite.Value.client);
        _encryption = new EncryptionService(EncryptionKey);

        var ownerTable = _tableStorage.GetTableClient(TableNames.Owners);
        await ownerTable.UpsertEntityAsync(new OwnerEntity
            { PartitionKey = "owner", RowKey = OwnerId, Name = "DelOwner", Email = "del@example.com" });
        await ownerTable.UpsertEntityAsync(new OwnerEntity
            { PartitionKey = "owner", RowKey = OtherId, Name = "OtherOwner", Email = "other@example.com" });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EventService EventSvc(string owner) =>
        new(new StubManagementAuthService(owner), _tableStorage, new NoopCacheInvalidationService());

    private ModelService ModelSvc(string owner) =>
        new(new StubManagementAuthService(owner), _tableStorage, _encryption, new NoopCacheInvalidationService());

    private EventEditorModel EventModel(string name = "Delete Test") => new()
    {
        Name = name, Description = "desc",
        Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow.AddHours(24),
        OrganizerName = "Org", OrganizerEmail = "org@example.com",
        MaxTokenCap = 4096, DailyRequestCap = 1024, Active = true,
        SelectedTimeZone = TimeZoneInfo.Utc
    };

    private ModelEditorModel ResourceModel(string deploymentName) => new()
    {
        FriendlyName = $"F-{deploymentName}", DeploymentName = deploymentName,
        EndpointUrl = "https://fake.openai.azure.com", EndpointKey = "key",
        Location = "eastus", Active = true, ModelType = ModelType.Foundry_Model,
        UseManagedIdentity = false, UseMaxCompletionTokens = false
    };

    // ── DeleteEventAsync silent failure tests ───────────────────────────

    [SkippableFact]
    public async Task DeleteEvent_WithAttendees_SilentlyRefuses()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG EXPOSURE: DeleteEventAsync returns Task (void) — caller cannot tell
        // that deletion was blocked because attendees exist.
        var evt = await EventSvc(OwnerId).CreateEventAsync(EventModel());

        // Add an attendee to block deletion
        var attendeeTable = _tableStorage.GetTableClient(TableNames.Attendees);
        await attendeeTable.AddEntityAsync(new AttendeeEntity
        {
            PartitionKey = evt!.EventId,
            RowKey = "user-1",
            ApiKey = Guid.NewGuid().ToString(),
            Active = true
        });

        // Delete completes without error — but does nothing
        await EventSvc(OwnerId).DeleteEventAsync(evt.EventId);

        // Event still exists — delete was silently blocked
        var fetched = await EventSvc(OwnerId).GetEventAsync(evt.EventId);
        Assert.NotNull(fetched); // If this were fixed, delete should throw or return false
    }

    [SkippableFact]
    public async Task DeleteEvent_WrongOwner_NoExceptionNoIndication()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG EXPOSURE: No exception, no return value when wrong owner tries to delete.
        // Compare with AddOwnerCatalogAsync which throws InvalidOperationException.
        var evt = await EventSvc(OwnerId).CreateEventAsync(EventModel());

        // Other owner tries to delete — completes silently
        await EventSvc(OtherId).DeleteEventAsync(evt!.EventId);

        // Event still exists
        Assert.NotNull(await EventSvc(OwnerId).GetEventAsync(evt.EventId));
    }

    [SkippableFact]
    public async Task DeleteEvent_NonExistentId_NoExceptionNoIndication()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG EXPOSURE: Deleting a non-existent event completes silently.
        // Caller cannot distinguish "deleted" from "didn't exist."
        await EventSvc(OwnerId).DeleteEventAsync("xxxx-fake");

        // No exception thrown — the UI would show success
    }

    // ── DeleteOwnerCatalogAsync silent failure tests ────────────────────

    [SkippableFact]
    public async Task DeleteResource_InUseByEvent_SilentlyRefuses()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG EXPOSURE: Resource deletion blocked by event linkage, with no indication
        var cat = await ModelSvc(OwnerId).AddOwnerCatalogAsync(ResourceModel($"dep-{_runId}-a"));
        var evt = await EventSvc(OwnerId).CreateEventAsync(EventModel());
        await EventSvc(OwnerId).UpdateModelsForEventAsync(evt!.EventId, [cat.CatalogId]);

        // Delete completes silently
        await ModelSvc(OwnerId).DeleteOwnerCatalogAsync(cat.CatalogId);

        // Resource still exists — delete was blocked
        Assert.NotNull(await ModelSvc(OwnerId).GetOwnerCatalogAsync(cat.CatalogId));
    }

    [SkippableFact]
    public async Task DeleteResource_WrongOwner_NoExceptionNoIndication()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var cat = await ModelSvc(OwnerId).AddOwnerCatalogAsync(ResourceModel($"dep-{_runId}-b"));

        // Other owner silently fails
        await ModelSvc(OtherId).DeleteOwnerCatalogAsync(cat.CatalogId);

        Assert.NotNull(await ModelSvc(OwnerId).GetOwnerCatalogAsync(cat.CatalogId));
    }

    [SkippableFact]
    public async Task DeleteResource_NonExistentId_NoExceptionNoIndication()
    {
        Skip.If(_azurite is null, "Azurite not available");

        await ModelSvc(OwnerId).DeleteOwnerCatalogAsync(Guid.NewGuid());
        // No exception — UI would show success
    }

    // ── Contrast with methods that DO report failures ────────────────

    [SkippableFact]
    public async Task AddResource_DuplicateName_ThrowsException_UnlikeDelete()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // This demonstrates the inconsistency: Add throws, Delete swallows.
        var name = $"dep-{_runId}-dup";
        await ModelSvc(OwnerId).AddOwnerCatalogAsync(ResourceModel(name));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModelSvc(OwnerId).AddOwnerCatalogAsync(ResourceModel(name)));

        Assert.Contains("already exists", ex.Message);
        // DeleteOwnerCatalogAsync has no equivalent error reporting.
    }
}
