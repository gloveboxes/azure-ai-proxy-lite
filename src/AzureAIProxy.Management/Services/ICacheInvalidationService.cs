namespace AzureAIProxy.Management.Services;

public interface ICacheInvalidationService
{
    Task InvalidateAllCachesAsync();
}
