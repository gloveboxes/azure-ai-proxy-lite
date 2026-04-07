using Microsoft.Extensions.Logging;

namespace AzureAIProxy.Management.Services;

public class CacheInvalidationService(HttpClient httpClient, IConfiguration configuration, ILogger<CacheInvalidationService> logger) : ICacheInvalidationService
{
    public async Task InvalidateAllCachesAsync()
    {
        var proxyUrl = configuration["ProxyInternalUrl"];
        if (string.IsNullOrEmpty(proxyUrl))
        {
            logger.LogWarning("ProxyInternalUrl is not configured. Skipping remote cache invalidation.");
            return;
        }

        var key = configuration["EncryptionKey"]
            ?? configuration["PostgresEncryptionKey"]
            ?? throw new InvalidOperationException("EncryptionKey must be configured for cache invalidation.");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyUrl.TrimEnd('/')}/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", key);

        try
        {
            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cache invalidation request returned {StatusCode}.", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail — caches will expire naturally
            logger.LogWarning(ex, "Failed to send cache invalidation request to proxy.");
        }
    }
}
