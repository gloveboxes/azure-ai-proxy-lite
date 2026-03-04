namespace AzureAIProxy.Management.Services;

public interface IAuthService
{
    Task<string> GetCurrentUserIdAsync();
    Task<(string email, string name)> GetCurrentUserEmailNameAsync();
}
