namespace AzureAIProxy.Shared.Database;

public class OwnerEventMap
{
    public string OwnerId { get; set; } = null!;
    public string EventId { get; set; } = null!;
    public bool Creator { get; set; }
}
