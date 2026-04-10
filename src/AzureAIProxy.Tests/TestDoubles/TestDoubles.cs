using System.Net;
using System.Text.Json;
using AzureAIProxy.Models;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using Microsoft.Extensions.Options;

namespace AzureAIProxy.Tests.TestDoubles;

internal sealed class StubAuthorizeService : IAuthorizeService
{
    public Func<string, Task<RequestContext?>> IsUserAuthorizedAsyncFunc { get; set; } = _ => Task.FromResult<RequestContext?>(null);
    public Func<string, Task<string?>> GetRequestContextFromJwtAsyncFunc { get; set; } = _ => Task.FromResult<string?>(null);

    public Task<RequestContext?> IsUserAuthorizedAsync(string apiKey) => IsUserAuthorizedAsyncFunc(apiKey);

    public Task<string?> GetRequestContextFromJwtAsync(string apiKey) => GetRequestContextFromJwtAsyncFunc(apiKey);
}

internal sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; } = currentValue;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}

internal sealed class StubRateLimitService : IRateLimitService
{
    public int RequestCountToReturn { get; set; }

    public int GetRequestCount(string apiKey) => RequestCountToReturn;

    public void IncrementUsage(string apiKey, int tokenCount)
    {
    }
}

internal sealed class NoopMetricService : IMetricService
{
    public int Calls { get; private set; }

    public Task LogApiUsageAsync(RequestContext requestContext, Deployment deployment, string? responseContent)
    {
        Calls++;
        return Task.CompletedTask;
    }
}

internal sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastContent { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastContent = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await responseFactory(request, cancellationToken);
    }
}

internal static class TestData
{
    public static RequestContext CreateRequestContext(
        string apiKey = "test-api-key",
        string eventId = "event-1",
        int maxTokenCap = 200,
        int dailyRequestCap = 100)
    {
        return new RequestContext
        {
            ApiKey = apiKey,
            EventId = eventId,
            UserId = "user-1",
            EventCode = "E001",
            OrganizerName = "Organizer",
            OrganizerEmail = "organizer@example.com",
            IsAuthorized = true,
            MaxTokenCap = maxTokenCap,
            DailyRequestCap = dailyRequestCap
        };
    }

    public static Deployment CreateDeployment(
        string modelType,
        bool useManagedIdentity = false,
        string endpointKey = "upstream-key")
    {
        return new Deployment
        {
            DeploymentName = "test-deployment",
            EndpointUrl = "https://upstream.example.com",
            EndpointKey = endpointKey,
            ModelType = modelType,
            UseManagedIdentity = useManagedIdentity,
            CatalogId = Guid.NewGuid(),
            Location = "eastus"
        };
    }

    public static JsonDocument ParseJson(string json) => JsonDocument.Parse(json);

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content) =>
        new(statusCode)
        {
            Content = new StringContent(content)
        };
}
