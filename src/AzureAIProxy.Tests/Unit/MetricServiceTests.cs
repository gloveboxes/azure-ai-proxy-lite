using System.Collections.Concurrent;
using Azure.Data.Tables;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Unit;

/// <summary>
/// Unit tests for MetricService covering token usage parsing from various JSON response formats.
/// </summary>
public class MetricServiceTests
{
    private static RateLimitService CreateRateLimiter()
    {
        var nullTableStorage = new NullTableStorageService();
        return new RateLimitService(nullTableStorage, NullLogger<RateLimitService>.Instance);
    }
    [Fact]
    public async Task LogApiUsageAsync_WithUsageTokens_IncrementsRateLimitCounter()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-1", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        var response = """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":20,"total_tokens":30}}""";

        await service.LogApiUsageAsync(context, deployment, response);

        Assert.Equal(1, rateLimiter.GetRequestCount("key-1"));
    }

    [Fact]
    public async Task LogApiUsageAsync_NullResponse_StillIncrementsRequestCount()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-2", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        await service.LogApiUsageAsync(context, deployment, null);

        Assert.Equal(1, rateLimiter.GetRequestCount("key-2"));
    }

    [Fact]
    public async Task LogApiUsageAsync_EmptyResponse_StillIncrementsRequestCount()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-3", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        await service.LogApiUsageAsync(context, deployment, "");

        Assert.Equal(1, rateLimiter.GetRequestCount("key-3"));
    }

    [Fact]
    public async Task LogApiUsageAsync_NoUsageProperty_StillEnqueuesMetric()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-4", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        var response = """{"choices":[{"message":{"content":"hello"}}]}""";

        await service.LogApiUsageAsync(context, deployment, response);

        Assert.Single(channel.Metrics);
        Assert.Equal(0, channel.Metrics[0].TotalTokens);
    }

    [Fact]
    public async Task LogApiUsageAsync_UsageWithNullValues_HandlesGracefully()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-5", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        var response = """{"usage":{"prompt_tokens":null,"completion_tokens":null,"total_tokens":null}}""";

        await service.LogApiUsageAsync(context, deployment, response);

        Assert.Single(channel.Metrics);
        Assert.Equal(0, channel.Metrics[0].TotalTokens);
    }

    [Fact]
    public async Task LogApiUsageAsync_InvalidJson_DoesNotThrow()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-6", "evt-1");
        var deployment = CreateDeployment("gpt-4o");

        await service.LogApiUsageAsync(context, deployment, "not json at all {{{");

        // Should still enqueue and increment
        Assert.Single(channel.Metrics);
        Assert.Equal(1, rateLimiter.GetRequestCount("key-6"));
    }

    [Fact]
    public async Task LogApiUsageAsync_EnqueuesCorrectResource()
    {
        var channel = new TestMetricChannel();
        var rateLimiter = CreateRateLimiter();
        var service = new MetricService(channel, rateLimiter);

        var context = CreateContext("key-7", "evt-res");
        var deployment = CreateDeployment("gpt-4o", "foundry-model");

        var response = """{"usage":{"prompt_tokens":5,"completion_tokens":15,"total_tokens":20}}""";

        await service.LogApiUsageAsync(context, deployment, response);

        Assert.Single(channel.Metrics);
        var metric = channel.Metrics[0];
        Assert.Equal("evt-res", metric.EventId);
        Assert.Equal("foundry-model | gpt-4o", metric.Resource);
        Assert.Equal(5, metric.PromptTokens);
        Assert.Equal(15, metric.CompletionTokens);
        Assert.Equal(20, metric.TotalTokens);
    }

    private static RequestContext CreateContext(string apiKey, string eventId) => new()
    {
        ApiKey = apiKey,
        EventId = eventId,
        UserId = "test-user",
        EventCode = "TEST",
        OrganizerName = "Test",
        OrganizerEmail = "t@t.com",
        MaxTokenCap = 500,
        DailyRequestCap = 1000,
        IsAuthorized = true
    };

    private static Deployment CreateDeployment(string name, string modelType = "foundry-model") => new()
    {
        DeploymentName = name,
        EndpointUrl = "https://fake.example.com",
        EndpointKey = "fake-key",
        ModelType = modelType,
        Location = "eastus"
    };

    /// <summary>
    /// Simple in-memory IMetricChannel that captures enqueued metrics.
    /// </summary>
    private class TestMetricChannel : IMetricChannel
    {
        public List<MetricUpdate> Metrics { get; } = [];

        public void Enqueue(MetricUpdate metric) => Metrics.Add(metric);
    }

    /// <summary>
    /// Stub ITableStorageService that throws if any table operation is attempted.
    /// RateLimitService only needs it for background flush which isn't triggered in unit tests.
    /// </summary>
    private class NullTableStorageService : ITableStorageService
    {
        public TableClient GetTableClient(string tableName) =>
            throw new NotSupportedException("NullTableStorageService: table operations not supported in unit tests");
    }
}
