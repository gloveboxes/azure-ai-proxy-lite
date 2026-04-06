using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace AzureAIProxy.Shared.Services;

public interface ICatalogCacheService
{
    MemoryCacheEntryOptions GetCacheEntryOptions();
    void InvalidateAll();
}

public class CatalogCacheService : ICatalogCacheService
{
    private CancellationTokenSource _cts = new();

    public MemoryCacheEntryOptions GetCacheEntryOptions()
    {
        return new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(30))
            .AddExpirationToken(new CancellationChangeToken(_cts.Token));
    }

    public void InvalidateAll()
    {
        var oldCts = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();
    }
}
