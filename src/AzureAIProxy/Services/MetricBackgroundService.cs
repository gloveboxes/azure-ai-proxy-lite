using System.Collections.Concurrent;
using System.Threading.Channels;
using Azure;
using Azure.Data.Tables;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzureAIProxy.Services;

public record MetricUpdate(string EventId, string Resource, int PromptTokens, int CompletionTokens, int TotalTokens, int RequestCount = 1);

public interface IMetricChannel
{
    void Enqueue(MetricUpdate update);
}

public class MetricBackgroundService : BackgroundService, IMetricChannel
{
    private readonly Channel<MetricUpdate> _channel = Channel.CreateBounded<MetricUpdate>(10_000);
    private readonly ITableStorageService _tableStorage;
    private readonly ILogger<MetricBackgroundService> _logger;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    public MetricBackgroundService(ITableStorageService tableStorage, ILogger<MetricBackgroundService> logger)
    {
        _tableStorage = tableStorage;
        _logger = logger;
    }

    public void Enqueue(MetricUpdate update)
    {
        if (!_channel.Writer.TryWrite(update))
        {
            _logger.LogWarning("Metric channel full, dropping metric for {EventId}/{Resource}", update.EventId, update.Resource);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var buffer = new ConcurrentDictionary<string, MetricUpdate>();

        using var flushTimer = new PeriodicTimer(FlushInterval);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in _channel.Reader.ReadAllAsync(stoppingToken))
                {
                    var key = $"{update.EventId}|{update.Resource}|{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}";
                    buffer.AddOrUpdate(
                        key,
                        update,
                        (_, existing) => new MetricUpdate(
                            existing.EventId,
                            existing.Resource,
                            existing.PromptTokens + update.PromptTokens,
                            existing.CompletionTokens + update.CompletionTokens,
                            existing.TotalTokens + update.TotalTokens,
                            existing.RequestCount + update.RequestCount
                        )
                    );
                }
            }
            catch (OperationCanceledException) { }
        }, stoppingToken);

        try
        {
            while (await flushTimer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await FlushBufferAsync(buffer);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Metric flush failed, will retry next interval");
                }
            }
        }
        catch (OperationCanceledException) { }

        // Final flush on shutdown
        await FlushBufferAsync(buffer);
    }

    private async Task FlushBufferAsync(ConcurrentDictionary<string, MetricUpdate> buffer)
    {
        if (buffer.IsEmpty) return;

        var table = _tableStorage.GetTableClient(TableNames.Metrics);
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        foreach (var kvp in buffer)
        {
            if (!buffer.TryRemove(kvp.Key, out var update)) continue;

            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var rowKey = $"{update.Resource}|{today}";
                    MetricEntity entity;
                    ETag etag;

                    try
                    {
                        var existing = await table.GetEntityAsync<MetricEntity>(update.EventId, rowKey);
                        entity = existing.Value;
                        entity.PromptTokens += update.PromptTokens;
                        entity.CompletionTokens += update.CompletionTokens;
                        entity.TotalTokens += update.TotalTokens;
                        entity.RequestCount += update.RequestCount;
                        etag = entity.ETag;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        entity = new MetricEntity
                        {
                            PartitionKey = update.EventId,
                            RowKey = rowKey,
                            Resource = update.Resource,
                            DateStamp = today,
                            PromptTokens = update.PromptTokens,
                            CompletionTokens = update.CompletionTokens,
                            TotalTokens = update.TotalTokens,
                            RequestCount = update.RequestCount
                        };
                        etag = ETag.All;
                    }

                    await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
                    break; // success
                }
                catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
                {
                    if (attempt == maxRetries - 1)
                        _logger.LogWarning(ex, "Failed to flush metric after {Attempts} attempts for {Key}", maxRetries, kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to flush metric for {Key}", kvp.Key);
                    break;
                }
            }
        }
    }
}
