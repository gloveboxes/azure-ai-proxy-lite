namespace AzureAIProxy.Shared.Database;

public class EventAttendee
{
    public string UserId { get; set; } = null!;
    public string EventId { get; set; } = null!;
    public bool Active { get; set; }
    public string ApiKey { get; set; } = null!;
}
