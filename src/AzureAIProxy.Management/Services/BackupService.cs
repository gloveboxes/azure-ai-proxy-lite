using Azure;
using Azure.Data.Tables;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Management.Services;

public class BackupService(ITableStorageService tableStorage, IEncryptionService encryption) : IBackupService
{
    public async Task<BackupData> CreateBackupAsync()
    {
        var backup = new BackupData
        {
            BackupTimestamp = DateTime.UtcNow
        };

        // Backup all events
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await foreach (var entity in eventsTable.QueryAsync<EventEntity>())
        {
            backup.Events.Add(new BackupEvent
            {
                EventId = entity.PartitionKey,
                OwnerId = entity.OwnerId,
                EventCode = entity.EventCode,
                EventSharedCode = entity.EventSharedCode,
                EventMarkdown = entity.EventMarkdown,
                StartTimestamp = entity.StartTimestamp,
                EndTimestamp = entity.EndTimestamp,
                TimeZoneOffset = entity.TimeZoneOffset,
                TimeZoneLabel = entity.TimeZoneLabel,
                OrganizerName = entity.OrganizerName,
                OrganizerEmail = entity.OrganizerEmail,
                EventImageUrl = entity.EventImageUrl,
                MaxTokenCap = entity.MaxTokenCap,
                DailyRequestCap = entity.DailyRequestCap,
                Active = entity.Active,
                CatalogIds = entity.CatalogIds
            });
        }

        // Backup all resources with decrypted endpoint and key
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await foreach (var entity in catalogTable.QueryAsync<CatalogEntity>())
        {
            string decryptedUrl;
            string decryptedKey;

            try { decryptedUrl = encryption.Decrypt(entity.EncryptedEndpointUrl); }
            catch { decryptedUrl = entity.EncryptedEndpointUrl; }

            try { decryptedKey = string.IsNullOrWhiteSpace(entity.EncryptedEndpointKey) ? string.Empty : encryption.Decrypt(entity.EncryptedEndpointKey); }
            catch { decryptedKey = entity.EncryptedEndpointKey; }

            backup.Resources.Add(new BackupResource
            {
                CatalogId = entity.PartitionKey,
                OwnerId = entity.OwnerId,
                DeploymentName = entity.DeploymentName,
                EndpointUrl = decryptedUrl,
                EndpointKey = decryptedKey,
                Active = entity.Active,
                ModelType = entity.ModelType,
                Location = entity.Location,
                FriendlyName = entity.FriendlyName,
                UseManagedIdentity = entity.UseManagedIdentity
            });
        }

        return backup;
    }

    public async Task RestoreBackupAsync(BackupData data)
    {
        // Restore resources first (events reference catalog IDs)
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        foreach (var resource in data.Resources)
        {
            var entity = new CatalogEntity
            {
                PartitionKey = resource.CatalogId,
                RowKey = resource.CatalogId,
                OwnerId = resource.OwnerId,
                DeploymentName = resource.DeploymentName,
                EncryptedEndpointUrl = encryption.Encrypt(resource.EndpointUrl),
                EncryptedEndpointKey = string.IsNullOrWhiteSpace(resource.EndpointKey) ? string.Empty : encryption.Encrypt(resource.EndpointKey),
                Active = resource.Active,
                ModelType = resource.ModelType,
                Location = resource.Location,
                FriendlyName = resource.FriendlyName,
                UseManagedIdentity = resource.UseManagedIdentity
            };

            await catalogTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);
        }

        // Restore events
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        var ownerEventsTable = tableStorage.GetTableClient(TableNames.OwnerEvents);

        foreach (var evt in data.Events)
        {
            var entity = new EventEntity
            {
                PartitionKey = evt.EventId,
                RowKey = evt.EventId,
                OwnerId = evt.OwnerId,
                EventCode = evt.EventCode,
                EventSharedCode = evt.EventSharedCode,
                EventMarkdown = evt.EventMarkdown,
                StartTimestamp = DateTime.SpecifyKind(evt.StartTimestamp, DateTimeKind.Utc),
                EndTimestamp = DateTime.SpecifyKind(evt.EndTimestamp, DateTimeKind.Utc),
                TimeZoneOffset = evt.TimeZoneOffset,
                TimeZoneLabel = evt.TimeZoneLabel,
                OrganizerName = evt.OrganizerName,
                OrganizerEmail = evt.OrganizerEmail,
                EventImageUrl = evt.EventImageUrl,
                MaxTokenCap = evt.MaxTokenCap,
                DailyRequestCap = evt.DailyRequestCap,
                Active = evt.Active,
                CatalogIds = evt.CatalogIds
            };

            await eventsTable.UpsertEntityAsync(entity, TableUpdateMode.Replace);

            // Restore owner-event mapping
            await ownerEventsTable.UpsertEntityAsync(new OwnerEventEntity
            {
                PartitionKey = evt.OwnerId,
                RowKey = evt.EventId,
                Creator = true
            }, TableUpdateMode.Replace);
        }
    }

    public async Task ClearAllDataAsync()
    {
        string[] tableNames =
        [
            TableNames.Events,
            TableNames.Attendees,
            TableNames.AttendeeLookup,
            TableNames.AttendeeRequests,
            TableNames.Metrics,
            TableNames.Catalogs,
            TableNames.OwnerEvents,
            TableNames.FoundryAgents
        ];

        // Delete all entities per table using batched transactions (max 100 per batch, same partition)
        foreach (var tableName in tableNames)
        {
            var table = tableStorage.GetTableClient(tableName);
            var entities = new List<TableEntity>();

            await foreach (var entity in table.QueryAsync<TableEntity>(select: new[] { "PartitionKey", "RowKey" }))
            {
                entities.Add(entity);
            }

            // Group by partition key for batch delete (Azure requires same partition per transaction)
            foreach (var group in entities.GroupBy(e => e.PartitionKey))
            {
                foreach (var batch in group.Chunk(100))
                {
                    var actions = batch.Select(e => new TableTransactionAction(TableTransactionActionType.Delete, e));
                    await table.SubmitTransactionAsync(actions);
                }
            }
        }
    }
}
