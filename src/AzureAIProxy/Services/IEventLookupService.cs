using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Services;

public interface IEventLookupService
{
    Task<EventEntity?> GetEventAsync(string eventId);
}
