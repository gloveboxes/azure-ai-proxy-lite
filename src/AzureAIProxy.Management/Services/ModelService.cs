using Azure;
using AzureAIProxy.Management.Components.ModelManagement;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Management.Services;

public class ModelService(IAuthService authService, ITableStorageService tableStorage, IEncryptionService encryption, ICacheInvalidationService cacheInvalidation) : IModelService
{
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    private async Task<bool> DeploymentNameExistsAsync(string deploymentName, Guid? excludeCatalogId = null)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await foreach (var entity in catalogTable.QueryAsync<CatalogEntity>(c => c.DeploymentName == deploymentName))
        {
            if (excludeCatalogId.HasValue && entity.PartitionKey == excludeCatalogId.Value.ToString())
                continue;
            return true;
        }
        return false;
    }

    private async Task<bool> FriendlyNameExistsAsync(string friendlyName)
    {
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await foreach (var _ in catalogTable.QueryAsync<CatalogEntity>(c => c.FriendlyName == friendlyName))
        {
            return true;
        }
        return false;
    }

    public async Task<OwnerCatalog> AddOwnerCatalogAsync(ModelEditorModel model)
    {
        string userId = await authService.GetCurrentUserIdAsync();

        var ownerTable = tableStorage.GetTableClient(TableNames.Owners);
        try { await ownerTable.GetEntityAsync<OwnerEntity>("owner", userId); }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException("User is not a registered owner.");
        }

        if (await DeploymentNameExistsAsync(model.DeploymentName!.Trim()))
            throw new InvalidOperationException($"A resource with deployment name '{model.DeploymentName!.Trim()}' already exists. Deployment names must be unique.");

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
            UseManagedIdentity = model.UseManagedIdentity,
            UseMaxCompletionTokens = model.UseMaxCompletionTokens
        };

        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);
        await catalogTable.AddEntityAsync(entity);

        await cacheInvalidation.InvalidateAllCachesAsync();

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
        await cacheInvalidation.InvalidateAllCachesAsync();
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
            UseManagedIdentity = entity.UseManagedIdentity,
            UseMaxCompletionTokens = entity.UseMaxCompletionTokens
        };
    }

    public async Task DuplicateOwnerCatalogAsync(OwnerCatalog ownerCatalog)
    {
        string userId = await authService.GetCurrentUserIdAsync();
        var catalogTable = tableStorage.GetTableClient(TableNames.Catalogs);

        var sourceId = ownerCatalog.CatalogId.ToString();
        var source = await catalogTable.GetEntityAsync<CatalogEntity>(sourceId, sourceId);

        // Generate a unique deployment name by appending "-copy", "-copy-2", etc.
        var baseDeploymentName = source.Value.DeploymentName;
        var newDeploymentName = $"{baseDeploymentName}-copy";
        int counter = 2;
        while (await DeploymentNameExistsAsync(newDeploymentName))
        {
            newDeploymentName = $"{baseDeploymentName}-copy-{counter}";
            counter++;
        }

        // Generate a unique friendly name by appending " (Copy)", " (Copy 2)", etc.
        var baseFriendlyName = source.Value.FriendlyName;
        var newFriendlyName = $"{baseFriendlyName} (Copy)";
        counter = 2;
        while (await FriendlyNameExistsAsync(newFriendlyName))
        {
            newFriendlyName = $"{baseFriendlyName} (Copy {counter})";
            counter++;
        }

        var newCatalogId = Guid.NewGuid().ToString();
        var entity = new CatalogEntity
        {
            PartitionKey = newCatalogId,
            RowKey = newCatalogId,
            OwnerId = userId,
            DeploymentName = newDeploymentName,
            Active = source.Value.Active,
            ModelType = source.Value.ModelType,
            Location = source.Value.Location,
            FriendlyName = newFriendlyName,
            EncryptedEndpointUrl = source.Value.EncryptedEndpointUrl,
            EncryptedEndpointKey = source.Value.EncryptedEndpointKey,
            UseManagedIdentity = source.Value.UseManagedIdentity,
            UseMaxCompletionTokens = source.Value.UseMaxCompletionTokens
        };

        await catalogTable.AddEntityAsync(entity);
        await cacheInvalidation.InvalidateAllCachesAsync();
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
                UseManagedIdentity = entity.UseManagedIdentity,
                UseMaxCompletionTokens = entity.UseMaxCompletionTokens
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

        if (existing.DeploymentName != ownerCatalog.DeploymentName.Trim()
            && await DeploymentNameExistsAsync(ownerCatalog.DeploymentName.Trim(), ownerCatalog.CatalogId))
            throw new InvalidOperationException($"A resource with deployment name '{ownerCatalog.DeploymentName.Trim()}' already exists. Deployment names must be unique.");

        existing.FriendlyName = ownerCatalog.FriendlyName;
        existing.DeploymentName = ownerCatalog.DeploymentName.Trim();
        existing.ModelType = ownerCatalog.ModelType!.Value.ToStorageString();
        existing.Location = ownerCatalog.Location;
        existing.Active = ownerCatalog.Active;
        existing.EncryptedEndpointUrl = encryption.Encrypt(ownerCatalog.EndpointUrl);
        existing.EncryptedEndpointKey = string.IsNullOrWhiteSpace(ownerCatalog.EndpointKey) ? string.Empty : encryption.Encrypt(ownerCatalog.EndpointKey);
        existing.UseManagedIdentity = ownerCatalog.UseManagedIdentity;
        existing.UseMaxCompletionTokens = ownerCatalog.UseMaxCompletionTokens;

        await catalogTable.UpdateEntityAsync(existing, existing.ETag, Azure.Data.Tables.TableUpdateMode.Replace);
        await cacheInvalidation.InvalidateAllCachesAsync();
    }
}
