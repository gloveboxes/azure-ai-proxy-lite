using Azure.Data.Tables;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using AzureAIProxy.Tests.Fixtures;
using AzureAIProxy.Tests.TestDoubles;

namespace AzureAIProxy.Tests.Admin;

public class BackupServiceTests : IAsyncLifetime
{
    private (TableServiceClient client, string connectionString)? _azurite;
    private ITableStorageService _tableStorage = null!;
    private IEncryptionService _encryption = null!;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];
    private string OwnerId => $"backup-owner-{_runId}";
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

    private BackupService CreateService(string? userId = null) =>
        new(_tableStorage, _encryption, new NoopCacheInvalidationService(),
            new StubManagementAuthService(userId ?? OwnerId));

    private EventService CreateEventService(string? userId = null) =>
        new(new StubManagementAuthService(userId ?? OwnerId), _tableStorage, new NoopCacheInvalidationService());

    private ModelService CreateModelService(string? userId = null) =>
        new(new StubManagementAuthService(userId ?? OwnerId), _tableStorage, _encryption, new NoopCacheInvalidationService());

    private async Task SeedOwner(string ownerId)
    {
        var table = _tableStorage.GetTableClient(TableNames.Owners);
        await table.UpsertEntityAsync(new OwnerEntity
        {
            PartitionKey = "owner",
            RowKey = ownerId,
            Name = "Test Owner",
            Email = $"{ownerId}@example.com"
        });
    }

    [SkippableFact]
    public async Task CreateBackup_EmptyData_ReturnsTimestampOnly()
    {
        Skip.If(_azurite is null, "Azurite not available");
        await SeedOwner(OwnerId);

        var service = CreateService();
        var backup = await service.CreateBackupAsync();

        Assert.NotEqual(default, backup.BackupTimestamp);
        Assert.Empty(backup.Events);
        Assert.Empty(backup.Resources);
        Assert.Empty(backup.Metrics);
        Assert.Empty(backup.Attendees);
    }

    [SkippableFact]
    public async Task BackupAndRestore_RoundTrip_PreservesData()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var userId = $"backup-roundtrip-{_runId}";
        await SeedOwner(userId);

        var eventService = CreateEventService(userId);
        var modelService = CreateModelService(userId);

        // Create a resource and event
        var catalog = await modelService.AddOwnerCatalogAsync(new()
        {
            FriendlyName = "Backup Model",
            DeploymentName = "backup-deploy",
            EndpointUrl = "https://backup.example.com",
            EndpointKey = "backup-key",
            Location = "westus",
            Active = true,
            ModelType = ModelType.Foundry_Model,
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        });

        var evt = await eventService.CreateEventAsync(new()
        {
            Name = "Backup Event",
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

        // Backup
        var backupService = CreateService(userId);
        var backup = await backupService.CreateBackupAsync();

        Assert.Single(backup.Events);
        Assert.Single(backup.Resources);
        Assert.Equal("Backup Event", backup.Events[0].EventCode);
        Assert.Equal("backup-deploy", backup.Resources[0].DeploymentName);
        Assert.Equal("https://backup.example.com", backup.Resources[0].EndpointUrl);

        // Clear and restore to a fresh user
        var restoreUserId = $"backup-restore-{_runId}";
        await SeedOwner(restoreUserId);
        var restoreService = CreateService(restoreUserId);
        await restoreService.RestoreBackupAsync(backup);

        // Verify restored data
        var restoredEventService = CreateEventService(restoreUserId);
        var restoredEvents = (await restoredEventService.GetOwnerEventsAsync()).ToList();
        Assert.Single(restoredEvents);
        Assert.Equal("Backup Event", restoredEvents[0].EventCode);

        var restoredModelService = CreateModelService(restoreUserId);
        var restoredModels = (await restoredModelService.GetOwnerCatalogsAsync()).ToList();
        Assert.Single(restoredModels);
        Assert.Equal("backup-deploy", restoredModels[0].DeploymentName);
    }

    [SkippableFact]
    public async Task EncryptedBackup_RoundTrip_DecryptsSuccessfully()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var userId = $"backup-encrypted-{_runId}";
        await SeedOwner(userId);

        var eventService = CreateEventService(userId);
        await eventService.CreateEventAsync(new()
        {
            Name = "Encrypted Event",
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

        var service = CreateService(userId);
        var passphrase = "my-super-secure-passphrase-123";
        var encrypted = await service.CreateEncryptedBackupAsync(passphrase);

        Assert.NotEmpty(encrypted);
        Assert.Equal(0x01, encrypted[0]); // Version byte

        // Restore to different user
        var restoreUserId = $"backup-enc-restore-{_runId}";
        await SeedOwner(restoreUserId);
        var restoreService = CreateService(restoreUserId);
        using var stream = new MemoryStream(encrypted);
        await restoreService.RestoreEncryptedBackupAsync(passphrase, stream);

        var restoredEvents = (await CreateEventService(restoreUserId).GetOwnerEventsAsync()).ToList();
        Assert.Single(restoredEvents);
        Assert.Equal("Encrypted Event", restoredEvents[0].EventCode);
    }

    [SkippableFact]
    public async Task EncryptedBackup_WrongPassphrase_Throws()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var userId = $"backup-wrongpass-{_runId}";
        await SeedOwner(userId);

        var service = CreateService(userId);
        var encrypted = await service.CreateEncryptedBackupAsync("correct-passphrase!");

        using var stream = new MemoryStream(encrypted);
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(
            () => service.RestoreEncryptedBackupAsync("wrong-passphrase!!", stream));
    }

    [SkippableFact]
    public async Task EncryptedBackup_ShortPassphrase_ThrowsArgumentException()
    {
        Skip.If(_azurite is null, "Azurite not available");

        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateEncryptedBackupAsync("short"));
    }

    [SkippableFact]
    public async Task ClearAllData_RemovesAllOwnedEntities()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var userId = $"backup-clear-{_runId}";
        await SeedOwner(userId);

        var eventService = CreateEventService(userId);
        var modelService = CreateModelService(userId);

        await modelService.AddOwnerCatalogAsync(new()
        {
            FriendlyName = "Clear Model",
            DeploymentName = "clear-deploy",
            EndpointUrl = "https://clear.example.com",
            EndpointKey = "clear-key",
            Location = "eastus",
            Active = true,
            ModelType = ModelType.Foundry_Model,
            UseManagedIdentity = false,
            UseMaxCompletionTokens = false
        });

        await eventService.CreateEventAsync(new()
        {
            Name = "Clear Event",
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

        var service = CreateService(userId);
        await service.ClearAllDataAsync();

        var events = (await eventService.GetOwnerEventsAsync()).ToList();
        var models = (await modelService.GetOwnerCatalogsAsync()).ToList();
        Assert.Empty(events);
        Assert.Empty(models);
    }

    [SkippableFact]
    public async Task Backup_OwnerIsolation_OnlyBackupsOwnData()
    {
        Skip.If(_azurite is null, "Azurite not available");
        var user1 = $"backup-iso-1-{_runId}";
        var user2 = $"backup-iso-2-{_runId}";
        await SeedOwner(user1);
        await SeedOwner(user2);

        await CreateEventService(user1).CreateEventAsync(new()
        {
            Name = "User1 Event",
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

        await CreateEventService(user2).CreateEventAsync(new()
        {
            Name = "User2 Event",
            Description = "desc",
            Start = DateTime.UtcNow.AddHours(-1),
            End = DateTime.UtcNow.AddHours(24),
            OrganizerName = "Org2",
            OrganizerEmail = "org2@example.com",
            MaxTokenCap = 4096,
            DailyRequestCap = 1024,
            Active = true,
            SelectedTimeZone = TimeZoneInfo.Utc
        });

        var backup1 = await CreateService(user1).CreateBackupAsync();
        var backup2 = await CreateService(user2).CreateBackupAsync();

        Assert.Single(backup1.Events);
        Assert.Equal("User1 Event", backup1.Events[0].EventCode);
        Assert.Single(backup2.Events);
        Assert.Equal("User2 Event", backup2.Events[0].EventCode);
    }
}
