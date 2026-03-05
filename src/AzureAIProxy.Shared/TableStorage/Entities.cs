using Azure;
using Azure.Data.Tables;

namespace AzureAIProxy.Shared.TableStorage;

public class EventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // event_id
    public string RowKey { get; set; } = null!;       // event_id (same as PK for point reads)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string OwnerId { get; set; } = null!;
    public string EventCode { get; set; } = null!;
    public string? EventSharedCode { get; set; }
    public string EventMarkdown { get; set; } = null!;
    public DateTime StartTimestamp { get; set; }
    public DateTime EndTimestamp { get; set; }
    public int TimeZoneOffset { get; set; }
    public string TimeZoneLabel { get; set; } = null!;
    public string OrganizerName { get; set; } = null!;
    public string OrganizerEmail { get; set; } = null!;
    public string? EventImageUrl { get; set; }
    public int MaxTokenCap { get; set; }
    public int DailyRequestCap { get; set; }
    public bool Active { get; set; }
    public string CatalogIds { get; set; } = "";  // comma-separated catalog GUIDs
}

public class AttendeeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // event_id
    public string RowKey { get; set; } = null!;       // user_id
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string ApiKey { get; set; } = null!;
    public bool Active { get; set; }
}

/// <summary>
/// Denormalized lookup: single point read for auth on every proxy request.
/// PK is first 2 hex chars of api_key for partition spread.
/// </summary>
public class AttendeeLookupEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // api_key[0..2]
    public string RowKey { get; set; } = null!;       // api_key
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Attendee fields
    public string EventId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public bool Active { get; set; }

    // Denormalized event fields (avoids second lookup on hot path)
    public string EventCode { get; set; } = null!;
    public string OrganizerName { get; set; } = null!;
    public string OrganizerEmail { get; set; } = null!;
    public string? EventImageUrl { get; set; }
    public int MaxTokenCap { get; set; }
    public int DailyRequestCap { get; set; }
    public bool EventActive { get; set; }
    public DateTime StartTimestamp { get; set; }
    public DateTime EndTimestamp { get; set; }
    public int TimeZoneOffset { get; set; }

    public static string GetPartitionKey(string apiKey) => apiKey[..2].ToLowerInvariant();
}

public class AttendeeRequestEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // api_key
    public string RowKey { get; set; } = null!;       // date (yyyy-MM-dd)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int RequestCount { get; set; }
    public int TokenCount { get; set; }
}

public class MetricEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // event_id
    public string RowKey { get; set; } = null!;       // resource|yyyy-MM-dd
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Resource { get; set; } = null!;
    public string DateStamp { get; set; } = null!;
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public long RequestCount { get; set; }
}

public class OwnerEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "owner"; // fixed partition (tiny table)
    public string RowKey { get; set; } = null!;         // owner_id
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
}

public class CatalogEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // catalog_id
    public string RowKey { get; set; } = null!;       // catalog_id (same as PK)
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string OwnerId { get; set; } = null!;
    public string DeploymentName { get; set; } = null!;
    public bool Active { get; set; }
    public string ModelType { get; set; } = null!;
    public string Location { get; set; } = null!;
    public string FriendlyName { get; set; } = null!;
    public string EncryptedEndpointUrl { get; set; } = null!;
    public string EncryptedEndpointKey { get; set; } = null!;
}

public class OwnerEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // owner_id
    public string RowKey { get; set; } = null!;       // event_id
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public bool Creator { get; set; }
}

public class AssistantEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // api_key
    public string RowKey { get; set; } = null!;       // assistant id
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
