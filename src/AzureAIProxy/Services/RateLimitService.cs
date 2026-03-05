using System.Collections.Concurrent;
using Azure;
using Azure.Data.Tables;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzureAIProxy.Services;

public record AttendeeUsage(int RequestCount, int TokenCount);

public interface IRateLimitService
{
    /// <summary>
    /// Gets the current request count for the given API key today.
    /// </summary>
    int GetRequestCount(string apiKey);

    /// <summary>
    /// Increments the request count and token count for the given API key today.
    /// </summary>
    void IncrementUsage(string apiKey, int tokenCount);
}

public class RateLimitService : IRateLimitService, IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, AttendeeUsage> _usage = new();
    private readonly ConcurrentDictionary<string, bool> _dirty = new();
    private readonly ITableStorageService _tableStorage;
    private readonly ILogger<RateLimitService> _logger;
    private Timer? _flushTimer;
    private Timer? _rolloverTimer;
    private DateOnly _currentDate = DateOnly.FromDateTime(DateTime.UtcNow);

    public RateLimitService(ITableStorageService tableStorage, ILogger<RateLimitService> logger)
    {
        _tableStorage = tableStorage;
        _logger = logger;
    }

    public int GetRequestCount(string apiKey)
    {
        CheckDateRollover();
        return _usage.TryGetValue(apiKey, out var usage) ? usage.RequestCount : 0;
    }

    public void IncrementUsage(string apiKey, int tokenCount)
    {
        CheckDateRollover();
        _usage.AddOrUpdate(
            apiKey,
            new AttendeeUsage(1, tokenCount),
            (_, existing) => new AttendeeUsage(existing.RequestCount + 1, existing.TokenCount + tokenCount)
        );
        _dirty[apiKey] = true;
    }

    private void CheckDateRollover()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today != _currentDate)
        {
            _currentDate = today;
            _usage.Clear();
            _dirty.Clear();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadFromStorageAsync();
        _flushTimer = new Timer(FlushCallback, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _rolloverTimer = new Timer(_ => CheckDateRollover(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _flushTimer?.Change(Timeout.Infinite, 0);
        _rolloverTimer?.Change(Timeout.Infinite, 0);
        FlushToStorage();
        return Task.CompletedTask;
    }

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var table = _tableStorage.GetTableClient(TableNames.AttendeeRequests);
            var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

            await foreach (var entity in table.QueryAsync<AttendeeRequestEntity>(e => e.RowKey == today))
            {
                _usage[entity.PartitionKey] = new AttendeeUsage(entity.RequestCount, entity.TokenCount);
            }
            _logger.LogInformation("Loaded {Count} rate limit entries from storage", _usage.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load rate limit data from storage, starting fresh");
        }
    }

    private void FlushCallback(object? state)
    {
        try { FlushToStorage(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to flush rate limit data"); }
    }

    private void FlushToStorage()
    {
        var table = _tableStorage.GetTableClient(TableNames.AttendeeRequests);
        var today = _currentDate.ToString("yyyy-MM-dd");

        foreach (var apiKey in _dirty.Keys)
        {
            if (!_dirty.TryRemove(apiKey, out _)) continue;
            if (!_usage.TryGetValue(apiKey, out var usage)) continue;

            var entity = new AttendeeRequestEntity
            {
                PartitionKey = apiKey,
                RowKey = today,
                RequestCount = usage.RequestCount,
                TokenCount = usage.TokenCount
            };

            try
            {
                table.UpsertEntity(entity, TableUpdateMode.Replace);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "Failed to flush rate limit for {ApiKey}", apiKey);
                _dirty[apiKey] = true; // retry next cycle
            }
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _rolloverTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
