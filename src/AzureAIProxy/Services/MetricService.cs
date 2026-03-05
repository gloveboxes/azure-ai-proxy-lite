using System.Text.Json;
using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Services;

public class MetricService(IMetricChannel metricChannel, IRateLimitService rateLimitService) : IMetricService
{
    public Task LogApiUsageAsync(RequestContext requestContext, Deployment deployment, string? responseContent)
    {
        var (promptTokens, completionTokens, totalTokens) = GetUsage(responseContent);

        // Update in-memory rate limit counters
        rateLimitService.IncrementUsage(requestContext.ApiKey, totalTokens);

        // Enqueue metric for background flush
        var resource = $"{deployment.ModelType} | {deployment.DeploymentName}";
        metricChannel.Enqueue(new MetricUpdate(requestContext.EventId, resource, promptTokens, completionTokens, totalTokens));

        return Task.CompletedTask;
    }

    private static (int promptTokens, int completionTokens, int totalTokens) GetUsage(string? responseContent)
    {
        if (string.IsNullOrEmpty(responseContent))
            return (0, 0, 0);

        try
        {
            using var jsonDoc = JsonDocument.Parse(responseContent);
            if (jsonDoc.RootElement.TryGetProperty("usage", out var usage))
            {
                var prompt = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
                var completion = usage.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0;
                var total = usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;
                return (prompt, completion, total);
            }
        }
        catch (JsonException) { }

        return (0, 0, 0);
    }
}
