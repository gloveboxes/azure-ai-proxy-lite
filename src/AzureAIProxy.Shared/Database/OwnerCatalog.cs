namespace AzureAIProxy.Shared.Database;

public class OwnerCatalog
{
    public string OwnerId { get; set; } = null!;
    public Guid CatalogId { get; set; }
    public string DeploymentName { get; set; } = null!;
    public string EndpointUrl { get; set; } = null!;
    public string EndpointKey { get; set; } = null!;
    public bool Active { get; set; }
    public ModelType? ModelType { get; set; }
    public string Location { get; set; } = null!;
    public string FriendlyName { get; set; } = null!;
    public bool UseManagedIdentity { get; set; }
    public List<Event> Events { get; set; } = [];
}
