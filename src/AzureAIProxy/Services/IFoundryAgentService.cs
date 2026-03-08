namespace AzureAIProxy.Services;

public interface IFoundryAgentService
{
    Task AddObjectAsync(string apiKey, string objectId, string objectType);
    Task DeleteObjectAsync(string apiKey, string objectId);
    Task<bool> ValidateObjectAsync(string apiKey, string objectId);
}
