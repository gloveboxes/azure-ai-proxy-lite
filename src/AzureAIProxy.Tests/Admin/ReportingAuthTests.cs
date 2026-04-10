using Azure.Data.Tables;
using AzureAIProxy.Management.Components.EventManagement;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;

namespace AzureAIProxy.Tests.Admin;

/// <summary>
/// Exposes the cross-tenant data exposure in the reporting layer.
/// MetricService has zero ownership checks — any authenticated user can view
/// any event's metrics, attendee counts, and full event details (organizer
/// name, email, shared code) simply by knowing/guessing the event ID.
///
/// Compare with EventService.GetEventAsync which returns null for wrong owner.
/// </summary>
public class ReportingAuthTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private string OwnerAlice => $"rpt-alice-{_runId}";
    private string OwnerBob => $"rpt-bob-{_runId}";

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
        if (_azurite is null) return;

        _tableStorage = new TableStorageService(_azurite.Value.client);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private EventService EventSvc(string owner) =>
        new(new StubManagementAuthService(owner), _tableStorage, new NoopCacheInvalidationService());

    private MetricService MetricSvc() => new(_tableStorage);

    private static EventEditorModel EventModel(string name, string email) => new()
    {
        Name = name, Description = "desc",
        Start = DateTime.UtcNow.AddHours(-1), End = DateTime.UtcNow.AddHours(24),
        OrganizerName = "Secret Org", OrganizerEmail = email,
        MaxTokenCap = 4096, DailyRequestCap = 1024, Active = true,
        SelectedTimeZone = TimeZoneInfo.Utc,
        EventSharedCode = "SecretCode123"
    };

    // ── EventService correctly denies cross-owner access ─────────────

    [SkippableFact]
    public async Task EventService_GetEvent_DeniesWrongOwner()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // Baseline: EventService correctly blocks cross-owner reads
        var evt = await EventSvc(OwnerAlice).CreateEventAsync(
            EventModel("Alice Private Event", "alice-secret@corp.com"));

        var bobResult = await EventSvc(OwnerBob).GetEventAsync(evt!.EventId);

        Assert.Null(bobResult);
    }

    // ── MetricService exposes the same data without any check ────────

    [SkippableFact]
    public async Task MetricService_GetEventForReport_ExposesAnyEventToAnyone()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG: MetricService.GetEventForReportAsync has no ownership check.
        // Any authenticated user with the event ID can see full event details.
        var evt = await EventSvc(OwnerAlice).CreateEventAsync(
            EventModel("Alice Private Event", "alice-secret@corp.com"));

        // MetricService has no owner context — it exposes everything
        var report = await MetricSvc().GetEventForReportAsync(evt!.EventId);

        Assert.NotNull(report);
        // These are Alice's private details, visible to anyone:
        Assert.Equal("alice-secret@corp.com", report.OrganizerEmail);
        Assert.Equal("Secret Org", report.OrganizerName);
        Assert.Equal("SecretCode123", report.EventSharedCode);
    }

    [SkippableFact]
    public async Task MetricService_GetAllEvents_LeaksOrganizerNamesAndEmails()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG: The global report page lists every event with organizer details.
        // In a multi-tenant admin panel, this leaks PII across tenants.
        await EventSvc(OwnerAlice).CreateEventAsync(
            EventModel("Alice Confidential", "alice@secret-corp.com"));
        await EventSvc(OwnerBob).CreateEventAsync(
            EventModel("Bob Confidential", "bob@other-corp.com"));

        var allEvents = await MetricSvc().GetAllEventsAsync();

        // Both owners' events visible with organizer names leaked
        var aliceEvent = allEvents.Find(e => e.OrganizerName == "Secret Org" && e.EventName == "Alice Confidential");
        var bobEvent = allEvents.Find(e => e.OrganizerName == "Secret Org" && e.EventName == "Bob Confidential");

        Assert.NotNull(aliceEvent);
        Assert.NotNull(bobEvent);
    }

    [SkippableFact]
    public async Task MetricService_GetEventMetrics_NoOwnershipCheck()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG: Any user who discovers an event ID can see its usage metrics.
        var evt = await EventSvc(OwnerAlice).CreateEventAsync(
            EventModel("Alice Usage Metrics", "alice@example.com"));

        // Seed metrics
        var metricTable = _tableStorage.GetTableClient(TableNames.Metrics);
        var dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await metricTable.UpsertEntityAsync(new MetricEntity
        {
            PartitionKey = evt!.EventId,
            RowKey = $"gpt-4o|{dateStamp}",
            Resource = "gpt-4o",
            DateStamp = dateStamp,
            PromptTokens = 50000,
            CompletionTokens = 100000,
            TotalTokens = 150000,
            RequestCount = 500
        });

        // MetricService exposes Alice's token usage to anyone
        var metrics = await MetricSvc().GetEventMetricsAsync(evt.EventId);

        Assert.Single(metrics);
        Assert.Equal(150000, metrics[0].TotalTokens);
        Assert.Equal(500, metrics[0].Requests);
    }

    [SkippableFact]
    public async Task MetricService_GetAttendeeMetrics_NoOwnershipCheck()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // BUG: Attendee count for any event is accessible without ownership.
        var evt = await EventSvc(OwnerAlice).CreateEventAsync(
            EventModel("Alice Attendees", "alice@example.com"));

        // Seed attendees
        var attendeeTable = _tableStorage.GetTableClient(TableNames.Attendees);
        for (int i = 0; i < 5; i++)
        {
            await attendeeTable.AddEntityAsync(new AttendeeEntity
            {
                PartitionKey = evt!.EventId,
                RowKey = $"user-{i}",
                ApiKey = Guid.NewGuid().ToString(),
                Active = true
            });
        }

        // Anyone can see how many attendees Alice's event has
        var (attendeeCount, _) = await MetricSvc().GetAttendeeMetricsAsync(evt!.EventId);

        Assert.Equal(5, attendeeCount);
    }
}
