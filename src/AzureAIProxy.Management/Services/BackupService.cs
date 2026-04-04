using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Management.Services;

public class BackupService(ITableStorageService tableStorage, IEncryptionService encryption) : IBackupService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 600_000;
    private const int KeySize = 32;
    private const byte BackupVersion = 0x01;
    public const int MinPassphraseLength = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
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

            decryptedUrl = encryption.Decrypt(entity.EncryptedEndpointUrl);
            decryptedKey = string.IsNullOrWhiteSpace(entity.EncryptedEndpointKey)
                ? string.Empty
                : encryption.Decrypt(entity.EncryptedEndpointKey);

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
                UseManagedIdentity = entity.UseManagedIdentity,
                UseMaxCompletionTokens = entity.UseMaxCompletionTokens
            });
        }

        return backup;
    }

    public async Task<byte[]> CreateEncryptedBackupAsync(string passphrase)
    {
        var data = await CreateBackupAsync();
        var json = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        return EncryptWithPassphrase(passphrase, json);
    }

    public async Task RestoreEncryptedBackupAsync(string passphrase, Stream encryptedStream)
    {
        using var ms = new MemoryStream();
        await encryptedStream.CopyToAsync(ms);
        var plainBytes = DecryptWithPassphrase(passphrase, ms.ToArray());
        var data = JsonSerializer.Deserialize<BackupData>(plainBytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid backup data.");
        await RestoreBackupAsync(data);
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
                UseManagedIdentity = resource.UseManagedIdentity,
                UseMaxCompletionTokens = resource.UseMaxCompletionTokens
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

    // Format: [version(1)] [salt(16)] [nonce(12)] [tag(16)] [ciphertext(N)]
    private static byte[] EncryptWithPassphrase(string passphrase, byte[] plainBytes)
    {
        ValidatePassphrase(passphrase);

        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[1 + SaltSize + NonceSize + TagSize + cipherBytes.Length];
        result[0] = BackupVersion;
        var span = result.AsSpan();
        salt.CopyTo(span.Slice(1, SaltSize));
        nonce.CopyTo(span.Slice(1 + SaltSize, NonceSize));
        tag.CopyTo(span.Slice(1 + SaltSize + NonceSize, TagSize));
        cipherBytes.CopyTo(span[(1 + SaltSize + NonceSize + TagSize)..]);

        return result;
    }

    private static byte[] DecryptWithPassphrase(string passphrase, byte[] fullBytes)
    {
        ValidatePassphrase(passphrase);

        const int headerSize = 1 + SaltSize + NonceSize + TagSize;
        if (fullBytes.Length < headerSize || fullBytes[0] != BackupVersion)
            throw new CryptographicException("Invalid or unsupported encrypted backup payload.");

        ReadOnlySpan<byte> fullSpan = fullBytes;
        var salt = fullSpan.Slice(1, SaltSize);
        var nonce = fullSpan.Slice(1 + SaltSize, NonceSize);
        var tag = fullSpan.Slice(1 + SaltSize + NonceSize, TagSize);
        var cipherBytes = fullSpan[headerSize..];
        var plainBytes = new byte[cipherBytes.Length];

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return plainBytes;
    }

    private static void ValidatePassphrase(string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        if (passphrase.Length < MinPassphraseLength)
            throw new ArgumentException($"Passphrase must be at least {MinPassphraseLength} characters.", nameof(passphrase));
    }
}
