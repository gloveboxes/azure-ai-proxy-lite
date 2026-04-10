using Azure.Data.Tables;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;

namespace AzureAIProxy.Tests.Admin;

public class AdminMetricServiceTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private IEncryptionService _encryption = null!;
    private readonly string OwnerId = $"metric-owner-{Guid.NewGuid():N}";
    private const string EncryptionKey = "dev-encryption-key-change-in-production";

    public async Task InitializeAsync()
    {
        _azurite = await AzuriteHelper.TryCreateLocalAzuriteClientWithConnectionStringAsync();
        if (_azurite is not null)
        {
            _tableStorage = new TableStorageService(_azurite.Value.client);
            _encryption = new EncryptionService(EncryptionKey);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MetricService CreateMetricService() => new(_tableStorage);

    private EventService CreateEventService(string? userId = null) =>
        new(new StubManagementAuthService(userId ?? OwnerId), _tableStorage, new NoopCacheInvalidationService());

    private async Task<string> SeedEventWithMetrics(string eventName, int promptTokens, int completionTokens, int requests)
    {
        // Create event via service
        var eventService = CreateEventService();
        var evt = await eventService.CreateEventAsync(new()
        {
            Name = eventName,
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

        // Seed metric data
        var metricTable = _tableStorage.GetTableClient(TableNames.Metrics);
        var dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await metricTable.UpsertEntityAsync(new MetricEntity
        {
            PartitionKey = evt!.EventId,
            RowKey = $"gpt-4o|{dateStamp}",
            Resource = "gpt-4o",
            DateStamp = dateStamp,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            RequestCount = requests
        });

        return evt.EventId;
    }

    [SkippableFact]
    public async Task GetEventMetrics_ReturnsSeededData()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var eventId = await SeedEventWithMetrics("Metric Event", 100, 200, 5);

        var service = CreateMetricService();
        var metrics = await service.GetEventMetricsAsync(eventId);

        Assert.Single(metrics);
        Assert.Equal(100, metrics[0].PromptTokens);
        Assert.Equal(200, metrics[0].CompletionTokens);
        Assert.Equal(300, metrics[0].TotalTokens);
        Assert.Equal(5, metrics[0].Requests);
        Assert.Equal("gpt-4o", metrics[0].Resource);
    }

    [SkippableFact]
    public async Task GetEventMetrics_NoData_ReturnsEmptyList()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var service = CreateMetricService();
        var metrics = await service.GetEventMetricsAsync("nonexistent-event");

        Assert.Empty(metrics);
    }

    [SkippableFact]
    public async Task GetAttendeeMetrics_ReturnsCountsFromSeededData()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var eventId = await SeedEventWithMetrics("Attendee Metric Event", 50, 100, 10);

        // Seed two attendees
        var attendeeTable = _tableStorage.GetTableClient(TableNames.Attendees);
        await attendeeTable.UpsertEntityAsync(new AttendeeEntity
        {
            PartitionKey = eventId,
            RowKey = "user-a",
            ApiKey = Guid.NewGuid().ToString(),
            Active = true
        });
        await attendeeTable.UpsertEntityAsync(new AttendeeEntity
        {
            PartitionKey = eventId,
            RowKey = "user-b",
            ApiKey = Guid.NewGuid().ToString(),
            Active = true
        });

        var service = CreateMetricService();
        var (attendeeCount, requestCount) = await service.GetAttendeeMetricsAsync(eventId);

        Assert.Equal(2, attendeeCount);
        Assert.Equal(10, requestCount);
    }

    [SkippableFact]
    public async Task GetEventForReport_ReturnsEventWithCatalogs()
    {
        Skip.If(_azurite is null, "Azurite not available");

        // Seed owner for catalog creation
        var ownerTable = _tableStorage.GetTableClient(TableNames.Owners);
        await ownerTable.UpsertEntityAsync(new OwnerEntity
        {
            PartitionKey = "owner",
            RowKey = OwnerId,
            Name = "Test Owner",
            Email = "owner@example.com"
        });

        var eventService = CreateEventService();
        var modelService = new ModelService(
            new StubManagementAuthService(OwnerId), _tableStorage, _encryption, new NoopCacheInvalidationService());

        var catalog = await modelService.AddOwnerCatalogAsync(new()
        {
            FriendlyName = "Report Model",
            DeploymentName = "report-deploy",
            EndpointUrl = "https://report.example.com",
            EndpointKey = "report-key",
            Location = "eastus",
            Active = true,
            ModelType = ModelType.Foundry_Model,
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        });

        var evt = await eventService.CreateEventAsync(new()
        {
            Name = "Report Event",
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

        var service = CreateMetricService();
        var report = await service.GetEventForReportAsync(evt.EventId);

        Assert.NotNull(report);
        Assert.Equal("Report Event", report.EventCode);
        Assert.Single(report.Catalogs);
        Assert.Equal("report-deploy", report.Catalogs[0].DeploymentName);
    }

    [SkippableFact]
    public async Task GetEventForReport_NonexistentEvent_ReturnsNull()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var service = CreateMetricService();
        var result = await service.GetEventForReportAsync("no-such-event");

        Assert.Null(result);
    }

    [SkippableFact]
    public async Task GetAllEvents_ReturnsEventsWithAttendeeCount()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var eventService = CreateEventService();
        var evt = await eventService.CreateEventAsync(new()
        {
            Name = "All Events Test",
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

        // Seed an attendee
        var attendeeTable = _tableStorage.GetTableClient(TableNames.Attendees);
        await attendeeTable.UpsertEntityAsync(new AttendeeEntity
        {
            PartitionKey = evt!.EventId,
            RowKey = "all-events-user",
            ApiKey = Guid.NewGuid().ToString(),
            Active = true
        });

        var service = CreateMetricService();
        var allEvents = await service.GetAllEventsAsync();

        var found = allEvents.FirstOrDefault(e => e.EventId == evt.EventId);
        Assert.NotNull(found);
        Assert.Equal("All Events Test", found.EventName);
        Assert.Equal(1, found.Registered);
    }

    [SkippableFact]
    public async Task GetActiveRegistrations_ReturnsCumulativeGrowth()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var eventService = CreateEventService();
        var evt = await eventService.CreateEventAsync(new()
        {
            Name = "Active Reg Test",
            Description = "desc",
            Start = DateTime.UtcNow.AddDays(-5),
            End = DateTime.UtcNow.AddHours(24),
            OrganizerName = "Org",
            OrganizerEmail = "org@example.com",
            MaxTokenCap = 4096,
            DailyRequestCap = 1024,
            Active = true,
            SelectedTimeZone = TimeZoneInfo.Utc
        });

        // Seed attendees with request history
        var attendeeTable = _tableStorage.GetTableClient(TableNames.Attendees);
        var requestTable = _tableStorage.GetTableClient(TableNames.AttendeeRequests);
        var apiKey1 = Guid.NewGuid().ToString();
        var apiKey2 = Guid.NewGuid().ToString();

        await attendeeTable.UpsertEntityAsync(new AttendeeEntity
        {
            PartitionKey = evt!.EventId,
            RowKey = "reg-user-1",
            ApiKey = apiKey1,
            Active = true
        });
        await attendeeTable.UpsertEntityAsync(new AttendeeEntity
        {
            PartitionKey = evt.EventId,
            RowKey = "reg-user-2",
            ApiKey = apiKey2,
            Active = true
        });

        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        await requestTable.UpsertEntityAsync(new AttendeeRequestEntity
        {
            PartitionKey = apiKey1,
            RowKey = today,
            RequestCount = 3,
            TokenCount = 500
        });
        await requestTable.UpsertEntityAsync(new AttendeeRequestEntity
        {
            PartitionKey = apiKey2,
            RowKey = today,
            RequestCount = 1,
            TokenCount = 100
        });

        var service = CreateMetricService();
        var registrations = await service.GetActiveRegistrationsAsync(evt.EventId);

        Assert.Single(registrations); // Both registered on same day
        Assert.Equal(2, registrations[0].Count); // Cumulative count
    }
}
