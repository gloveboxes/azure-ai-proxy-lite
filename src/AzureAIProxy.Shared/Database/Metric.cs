namespace AzureAIProxy.Shared.Database;

public partial class Metric
{
    public string EventId { get; set; } = null!;

    public string Resource { get; set; } = null!;

    public DateOnly DateStamp { get; set; }

    public long PromptTokens { get; set; }

    public long CompletionTokens { get; set; }

    public long TotalTokens { get; set; }

    public long RequestCount { get; set; }

    public virtual Event Event { get; set; } = null!;
}
