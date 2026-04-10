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
/// Verifies that two owners with identically-named events/resources are
/// fully isolated from each other at every service layer — except reporting,
/// which intentionally surfaces events from all owners.
/// </summary>
public class OwnerIsolationTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private IEncryptionService _encryption = null!;
    private const string EncryptionKey = "dev-encryption-key-change-in-production";

    // Two owners with unique IDs per run
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private string OwnerAlice => $"alice-{_runId}";
    private string OwnerBob => $"bob-{_runId}";

    // Shared names used by both owners — the whole point of the test
    private const string SharedDeploymentName = "gpt-4o-shared";
    private const string SharedFriendlyName = "GPT-4o Shared";
    private const string SharedEventName = "AI Workshop";

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
        if (_azurite is null) return;

        _tableStorage = new TableStorageService(_azurite.Value.client);
        _encryption = new EncryptionService(EncryptionKey);

        // Seed both owner records (required by ModelService)
        var ownerTable = _tableStorage.GetTableClient(TableNames.Owners);
        await ownerTable.UpsertEntityAsync(new OwnerEntity
        {
            PartitionKey = "owner", RowKey = OwnerAlice,
            Name = "Alice", Email = "alice@example.com"
        });
        await ownerTable.UpsertEntityAsync(new OwnerEntity
        {
            PartitionKey = "owner", RowKey = OwnerBob,
            Name = "Bob", Email = "bob@example.com"
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Service factories ───────────────────────────────────────────────

    private EventService EventSvc(string owner) =>
        new(new StubManagementAuthService(owner), _tableStorage, new NoopCacheInvalidationService());

    private ModelService ModelSvc(string owner) =>
        new(new StubManagementAuthService(owner), _tableStorage, _encryption, new NoopCacheInvalidationService());

    private BackupService BackupSvc(string owner) =>
        new(_tableStorage, _encryption, new NoopCacheInvalidationService(), new StubManagementAuthService(owner));

    private MetricService MetricSvc() => new(_tableStorage);

    // ── Helpers ─────────────────────────────────────────────────────────

    private static ModelEditorModel ResourceModel() => new()
    {
        FriendlyName = SharedFriendlyName,
        DeploymentName = SharedDeploymentName,
        EndpointUrl = "https://shared.openai.azure.com",
        EndpointKey = "shared-key",
        Location = "eastus",
        Active = true,
        ModelType = ModelType.Foundry_Model,
        UseManagedIdentity = false,
        UseMaxCompletionTokens = false
    };

    private static EventEditorModel EventModel() => new()
    {
        Name = SharedEventName,
        Description = "Same description for both owners",
        Start = DateTime.UtcNow.AddHours(-1),
        End = DateTime.UtcNow.AddHours(24),
        OrganizerName = "Same Org",
        OrganizerEmail = "org@example.com",
        MaxTokenCap = 4096,
        DailyRequestCap = 1024,
        Active = true,
        SelectedTimeZone = TimeZoneInfo.Utc
    };

    // ── Tests ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task BothOwners_CanCreateResource_WithSameDeploymentName()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var catAlice = await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        var catBob = await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());

        Assert.NotEqual(catAlice.CatalogId, catBob.CatalogId);
        Assert.Equal(SharedDeploymentName, catAlice.DeploymentName);
        Assert.Equal(SharedDeploymentName, catBob.DeploymentName);
    }

    [SkippableFact]
    public async Task BothOwners_CanCreateEvent_WithSameName()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        var evtBob = await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        Assert.NotEqual(evtAlice!.EventId, evtBob!.EventId);
        Assert.Equal(SharedEventName, evtAlice.EventCode);
        Assert.Equal(SharedEventName, evtBob.EventCode);
    }

    [SkippableFact]
    public async Task Owner_OnlySeesOwnResources()
    {
        Skip.If(_azurite is null, "Azurite not available");

        await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());

        var aliceModels = (await ModelSvc(OwnerAlice).GetOwnerCatalogsAsync()).ToList();
        var bobModels = (await ModelSvc(OwnerBob).GetOwnerCatalogsAsync()).ToList();

        Assert.Single(aliceModels);
        Assert.Single(bobModels);
        Assert.NotEqual(aliceModels[0].CatalogId, bobModels[0].CatalogId);
        Assert.All(aliceModels, m => Assert.Equal(OwnerAlice, m.OwnerId));
        Assert.All(bobModels, m => Assert.Equal(OwnerBob, m.OwnerId));
    }

    [SkippableFact]
    public async Task Owner_OnlySeesOwnEvents()
    {
        Skip.If(_azurite is null, "Azurite not available");

        await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        var aliceEvents = (await EventSvc(OwnerAlice).GetOwnerEventsAsync()).ToList();
        var bobEvents = (await EventSvc(OwnerBob).GetOwnerEventsAsync()).ToList();

        Assert.Single(aliceEvents);
        Assert.Single(bobEvents);
        Assert.NotEqual(aliceEvents[0].EventId, bobEvents[0].EventId);
    }

    [SkippableFact]
    public async Task Owner_CannotGetOtherOwnerResource()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var catAlice = await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());

        // Bob tries to fetch Alice's catalog by ID
        var result = await ModelSvc(OwnerBob).GetOwnerCatalogAsync(catAlice.CatalogId);

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task Owner_CannotGetOtherOwnerEvent()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());

        // Bob tries to fetch Alice's event by ID
        var result = await EventSvc(OwnerBob).GetEventAsync(evtAlice!.EventId);

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task Owner_CannotUpdateOtherOwnerEvent()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());

        var hijacked = EventModel();
        hijacked.Name = "Hijacked!";
        var result = await EventSvc(OwnerBob).UpdateEventAsync(evtAlice!.EventId, hijacked);

        Assert.Null(result);

        // Verify Alice's event is unchanged
        var original = await EventSvc(OwnerAlice).GetEventAsync(evtAlice.EventId);
        Assert.Equal(SharedEventName, original!.EventCode);
    }

    [SkippableFact]
    public async Task Owner_CannotDeleteOtherOwnerEvent()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());

        // Bob tries to delete — should silently fail (not found for Bob)
        await EventSvc(OwnerBob).DeleteEventAsync(evtAlice!.EventId);

        // Alice's event still exists
        var fetched = await EventSvc(OwnerAlice).GetEventAsync(evtAlice.EventId);
        Assert.NotNull(fetched);
    }

    [SkippableFact]
    public async Task DeletingOneOwnerResource_DoesNotAffectOther()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var catAlice = await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        var catBob = await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());

        await ModelSvc(OwnerAlice).DeleteOwnerCatalogAsync(catAlice.CatalogId);

        // Alice's is gone
        Assert.Null(await ModelSvc(OwnerAlice).GetOwnerCatalogAsync(catAlice.CatalogId));
        // Bob's still exists
        Assert.NotNull(await ModelSvc(OwnerBob).GetOwnerCatalogAsync(catBob.CatalogId));
    }

    [SkippableFact]
    public async Task DeletingOneOwnerEvent_DoesNotAffectOther()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        var evtBob = await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        await EventSvc(OwnerAlice).DeleteEventAsync(evtAlice!.EventId);

        Assert.Null(await EventSvc(OwnerAlice).GetEventAsync(evtAlice.EventId));
        Assert.NotNull(await EventSvc(OwnerBob).GetEventAsync(evtBob!.EventId));
    }

    [SkippableFact]
    public async Task Backup_OnlyContainsOwnData_WhenNamesMatch()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // Both create identically-named resource + event
        await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());
        await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        var aliceBackup = await BackupSvc(OwnerAlice).CreateBackupAsync();
        var bobBackup = await BackupSvc(OwnerBob).CreateBackupAsync();

        Assert.Single(aliceBackup.Events);
        Assert.Single(aliceBackup.Resources);
        Assert.Single(bobBackup.Events);
        Assert.Single(bobBackup.Resources);

        // Events are different IDs despite same name
        Assert.NotEqual(aliceBackup.Events[0].EventId, bobBackup.Events[0].EventId);
        Assert.NotEqual(aliceBackup.Resources[0].CatalogId, bobBackup.Resources[0].CatalogId);
    }

    [SkippableFact]
    public async Task ClearAllData_OnlyAffectsCallingOwner()
    {
        Skip.If(_azurite is null, "Azurite not available");

        await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());
        await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        // Alice clears her data
        await BackupSvc(OwnerAlice).ClearAllDataAsync();

        // Alice sees nothing
        Assert.Empty(await EventSvc(OwnerAlice).GetOwnerEventsAsync());
        Assert.Empty(await ModelSvc(OwnerAlice).GetOwnerCatalogsAsync());

        // Bob is unaffected
        var bobEvents = (await EventSvc(OwnerBob).GetOwnerEventsAsync()).ToList();
        var bobModels = (await ModelSvc(OwnerBob).GetOwnerCatalogsAsync()).ToList();
        Assert.Single(bobEvents);
        Assert.Single(bobModels);
    }

    [SkippableFact]
    public async Task LinkingCatalogsToEvent_IsIsolatedPerOwner()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var catAlice = await ModelSvc(OwnerAlice).AddOwnerCatalogAsync(ResourceModel());
        var catBob = await ModelSvc(OwnerBob).AddOwnerCatalogAsync(ResourceModel());
        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        var evtBob = await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        await EventSvc(OwnerAlice).UpdateModelsForEventAsync(evtAlice!.EventId, [catAlice.CatalogId]);
        await EventSvc(OwnerBob).UpdateModelsForEventAsync(evtBob!.EventId, [catBob.CatalogId]);

        // Verify through reporting which has catalog details
        var aliceReport = await MetricSvc().GetEventForReportAsync(evtAlice.EventId);
        var bobReport = await MetricSvc().GetEventForReportAsync(evtBob.EventId);

        // Each event is linked to its owner's catalog, not the other's
        Assert.Single(aliceReport!.Catalogs);
        Assert.Equal(catAlice.CatalogId, aliceReport.Catalogs[0].CatalogId);
        Assert.Single(bobReport!.Catalogs);
        Assert.Equal(catBob.CatalogId, bobReport.Catalogs[0].CatalogId);
    }

    // ── Reporting exception: everyone sees all events ───────────────────

    [SkippableFact]
    public async Task Reporting_GetAllEvents_ShowsEventsFromAllOwners()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());
        var evtBob = await EventSvc(OwnerBob).CreateEventAsync(EventModel());

        var allEvents = await MetricSvc().GetAllEventsAsync();

        // Both owners' events appear in the global report
        Assert.Contains(allEvents, e => e.EventId == evtAlice!.EventId);
        Assert.Contains(allEvents, e => e.EventId == evtBob!.EventId);
    }

    [SkippableFact]
    public async Task Reporting_GetEventMetrics_AccessibleRegardlessOfOwner()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());

        // Seed metrics for Alice's event
        var metricTable = _tableStorage.GetTableClient(TableNames.Metrics);
        var dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await metricTable.UpsertEntityAsync(new MetricEntity
        {
            PartitionKey = evtAlice!.EventId,
            RowKey = $"{SharedDeploymentName}|{dateStamp}",
            Resource = SharedDeploymentName,
            DateStamp = dateStamp,
            PromptTokens = 100,
            CompletionTokens = 200,
            TotalTokens = 300,
            RequestCount = 5
        });

        // MetricService has no owner scoping — anyone with the event ID can see metrics
        var metrics = await MetricSvc().GetEventMetricsAsync(evtAlice.EventId);

        Assert.Single(metrics);
        Assert.Equal(300, metrics[0].TotalTokens);
    }

    [SkippableFact]
    public async Task Reporting_GetEventForReport_AccessibleRegardlessOfOwner()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var evtAlice = await EventSvc(OwnerAlice).CreateEventAsync(EventModel());

        // MetricService.GetEventForReportAsync has no owner check
        var report = await MetricSvc().GetEventForReportAsync(evtAlice!.EventId);

        Assert.NotNull(report);
        Assert.Equal(SharedEventName, report.EventCode);
    }
}
