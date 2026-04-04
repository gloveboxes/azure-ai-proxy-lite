namespace AzureAIProxy.Shared.Database;

public class RequestContext
{
    public bool IsAuthorized { get; set; }
    public string ApiKey { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string EventId { get; set; } = null!;
    public string EventCode { get; set; } = null!;
    public string OrganizerName { get; set; } = null!;
    public string OrganizerEmail { get; set; } = null!;
    public int MaxTokenCap { get; set; }
    public int DailyRequestCap { get; set; }
    public bool RateLimitExceed { get; set; }
}
