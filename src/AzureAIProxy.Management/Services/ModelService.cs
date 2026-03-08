using Azure;
using AzureAIProxy.Management.Components.ModelManagement;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Management.Services;

public class ModelService(IAuthService authService, ITableStorageService tableStorage, IEncryptionService encryption) : IModelService
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    public async Task<OwnerCatalog> AddOwnerCatalogAsync(ModelEditorModel model)
    {
        string userId = await authService.GetCurrentUserIdAsync();

        var ownerTable = tableStorage.GetTableClient(TableNames.Owners);
        try { await ownerTable.GetEntityAsync<OwnerEntity>("owner", userId); }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException("User is not a registered owner.");
        }

        var catalogId = Guid.NewGuid().ToString();
        var entity = new CatalogEntity
        {
            PartitionKey = catalogId,
            RowKey = catalogId,
            OwnerId = userId,
            DeploymentName = model.DeploymentName!.Trim(),
            Active = model.Active,
            ModelType = model.ModelType!.Value.ToStorageString(),
            Location = model.Location!,
            FriendlyName = model.FriendlyName!,
            EncryptedEndpointUrl = encryption.Encrypt(model.EndpointUrl!),
            EncryptedEndpointKey = string.IsNullOrWhiteSpace(model.EndpointKey) ? string.Empty : encryption.Encrypt(model.EndpointKey),
            UseManagedIdentity = model.UseManagedIdentity
        };

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await catalogTable.AddEntityAsync(entity);

        return new OwnerCatalog
        {
            OwnerId = userId,
            CatalogId = Guid.Parse(catalogId),
            DeploymentName = entity.DeploymentName,
            Active = entity.Active,
            ModelType = model.ModelType!.Value,
            Location = entity.Location,
            FriendlyName = entity.FriendlyName
        };
    }

    public async Task DeleteOwnerCatalogAsync(Guid catalogId)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var id = catalogId.ToString();

        try
        {
            await catalogTable.GetEntityAsync<CatalogEntity>(id, id);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return;
        }

        // Check if used in any events by scanning events for this catalog ID
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);
        await foreach (var evt in eventsTable.QueryAsync<EventEntity>())
        {
            if (!string.IsNullOrEmpty(evt.CatalogIds) && evt.CatalogIds.Split(',').Contains(id))
                return; // Block deletion when in use
        }

        await catalogTable.DeleteEntityAsync(id, id);
    }

    public async Task<OwnerCatalog> GetOwnerCatalogAsync(Guid catalogId)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var id = catalogId.ToString();
        var response = await catalogTable.GetEntityAsync<CatalogEntity>(id, id);
        var entity = response.Value;

        return new OwnerCatalog
        {
            OwnerId = entity.OwnerId,
            CatalogId = Guid.Parse(id),
            DeploymentName = entity.DeploymentName,
            Active = entity.Active,
            ModelType = ModelTypeExtensions.FromStorageString(entity.ModelType),
            Location = entity.Location,
            FriendlyName = entity.FriendlyName,
            EndpointUrl = encryption.Decrypt(entity.EncryptedEndpointUrl),
            EndpointKey = string.IsNullOrWhiteSpace(entity.EncryptedEndpointKey) ? string.Empty : encryption.Decrypt(entity.EncryptedEndpointKey),
            UseManagedIdentity = entity.UseManagedIdentity
        };
    }

    public async Task DuplicateOwnerCatalogAsync(OwnerCatalog ownerCatalog)
    {
        string userId = await authService.GetCurrentUserIdAsync();
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);

        var sourceId = ownerCatalog.CatalogId.ToString();
        var source = await catalogTable.GetEntityAsync<CatalogEntity>(sourceId, sourceId);

        var newCatalogId = Guid.NewGuid().ToString();
        var entity = new CatalogEntity
        {
            PartitionKey = newCatalogId,
            RowKey = newCatalogId,
            OwnerId = userId,
            DeploymentName = source.Value.DeploymentName,
            Active = source.Value.Active,
            ModelType = source.Value.ModelType,
            Location = source.Value.Location,
            FriendlyName = $"{source.Value.FriendlyName} (Copy)",
            EncryptedEndpointUrl = source.Value.EncryptedEndpointUrl,
            EncryptedEndpointKey = source.Value.EncryptedEndpointKey,
            UseManagedIdentity = source.Value.UseManagedIdentity
        };

        await catalogTable.AddEntityAsync(entity);
    }

    public async Task<IEnumerable<OwnerCatalog>> GetOwnerCatalogsAsync()
    {
        string userId = await authService.GetCurrentUserIdAsync();
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var eventsTable = tableStorage.GetTableClient(TableNames.Events);

        // Get all catalogs owned by this user
        var results = new List<OwnerCatalog>();
        await foreach (var entity in catalogTable.QueryAsync<CatalogEntity>(c => c.OwnerId == userId))
        {
            var catalog = new OwnerCatalog
            {
                OwnerId = entity.OwnerId,
                CatalogId = Guid.Parse(entity.PartitionKey),
                DeploymentName = entity.DeploymentName,
                Active = entity.Active,
                ModelType = ModelTypeExtensions.FromStorageString(entity.ModelType),
                Location = entity.Location,
                FriendlyName = entity.FriendlyName,
                UseManagedIdentity = entity.UseManagedIdentity
            };

            // Check which events reference this catalog
            await foreach (var evt in eventsTable.QueryAsync<EventEntity>())
            {
                if (!string.IsNullOrEmpty(evt.CatalogIds) && evt.CatalogIds.Split(',').Contains(entity.PartitionKey))
                {
                    catalog.Events.Add(new Event { EventId = evt.PartitionKey });
                }
            }

            results.Add(catalog);
        }

        return results.OrderBy(c => c.FriendlyName);
    }

    public async Task UpdateOwnerCatalogAsync(OwnerCatalog ownerCatalog)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        var id = ownerCatalog.CatalogId.ToString();

        CatalogEntity existing;
        try
        {
            var response = await catalogTable.GetEntityAsync<CatalogEntity>(id, id);
            existing = response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return;
        }

        existing.FriendlyName = ownerCatalog.FriendlyName;
        existing.DeploymentName = ownerCatalog.DeploymentName.Trim();
        existing.ModelType = ownerCatalog.ModelType!.Value.ToStorageString();
        existing.Location = ownerCatalog.Location;
        existing.Active = ownerCatalog.Active;
        existing.EncryptedEndpointUrl = encryption.Encrypt(ownerCatalog.EndpointUrl);
        existing.EncryptedEndpointKey = string.IsNullOrWhiteSpace(ownerCatalog.EndpointKey) ? string.Empty : encryption.Encrypt(ownerCatalog.EndpointKey);
        existing.UseManagedIdentity = ownerCatalog.UseManagedIdentity;

        await catalogTable.UpdateEntityAsync(existing, existing.ETag, Azure.Data.Tables.TableUpdateMode.Replace);
    }
}
