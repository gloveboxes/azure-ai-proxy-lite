using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Services;

public interface ICatalogService
{
    Task<(Deployment? deployment, List<Deployment> eventCatalog)> GetCatalogItemAsync(
        string eventId,
        string deploymentName
    );
    Task<Deployment?> GetEventFoundryAgentAsync(string eventId);
    Task<Deployment?> GetEventMcpServerAsync(string eventId, string deploymentName);
    Task<Dictionary<string, List<string>>> GetCapabilitiesAsync(string eventId);
    Task<List<string>> GetAiToolkitDeploymentsAsync(string eventId);
}
