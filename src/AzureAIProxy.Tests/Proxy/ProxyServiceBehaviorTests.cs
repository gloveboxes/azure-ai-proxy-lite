using System.Net;
using System.Text.Json;
using AzureAIProxy.Models;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Proxy;

public class ProxyServiceBehaviorTests
{
    [Fact]
    public async Task GetAuthenticationHeaderAsync_EndpointKeyMode_UsesApiKeyHeader()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(TestData.JsonResponse(HttpStatusCode.OK, "{}")));

        var service = new ProxyService(
            new StubHttpClientFactory(new HttpClient(handler)),
            new NoopMetricService(),
            NullLogger<ProxyService>.Instance);

        var deployment = TestData.CreateDeployment(ModelType.Foundry_Model.ToStorageString(), useManagedIdentity: false, endpointKey: "secret-key");

        var header = await service.GetAuthenticationHeaderAsync(deployment, useBearerToken: false);

        Assert.Equal("api-key", header.Key);
        Assert.Equal("secret-key", header.Value);
    }

    [Fact]
    public async Task GetAuthenticationHeaderAsync_BearerMode_UsesAuthorizationHeader()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(TestData.JsonResponse(HttpStatusCode.OK, "{}")));

        var service = new ProxyService(
            new StubHttpClientFactory(new HttpClient(handler)),
            new NoopMetricService(),
            NullLogger<ProxyService>.Instance);

        var deployment = TestData.CreateDeployment(ModelType.Foundry_Model.ToStorageString(), useManagedIdentity: false, endpointKey: "bearer-token");

        var header = await service.GetAuthenticationHeaderAsync(deployment, useBearerToken: true);

        Assert.Equal("Authorization", header.Key);
        Assert.Equal("Bearer bearer-token", header.Value);
    }

    [Fact]
    public async Task HttpPostAsync_FoundryToolkit_RewritesMaxTokensToMaxCompletionTokens()
    {
        var handler = new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(TestData.JsonResponse(HttpStatusCode.OK, "{\"ok\":true}")));
        var httpClient = new HttpClient(handler);
        var metricService = new NoopMetricService();

        var service = new ProxyService(
            new StubHttpClientFactory(httpClient),
            metricService,
            NullLogger<ProxyService>.Instance);

        var deployment = TestData.CreateDeployment(ModelType.Foundry_Toolkit.ToStorageString(), useManagedIdentity: false);
        deployment.UseMaxCompletionTokens = true;

        using var body = JsonDocument.Parse("{\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"max_tokens\":123}");
        var context = new DefaultHttpContext();

        var (responseContent, statusCode) = await service.HttpPostAsync(
            new UriBuilder("https://upstream.example.com/openai/deployments/test/chat/completions?api-version=2024-10-21"),
            [new RequestHeader("api-key", "proxy-key")],
            context,
            body,
            TestData.CreateRequestContext(),
            deployment);

        Assert.Equal(200, statusCode);
        Assert.Equal("{\"ok\":true}", responseContent);
        Assert.Equal(1, metricService.Calls);

        Assert.NotNull(handler.LastContent);
        using var rewrittenBody = JsonDocument.Parse(handler.LastContent!);
        Assert.True(rewrittenBody.RootElement.TryGetProperty("max_completion_tokens", out var maxCompletionTokens));
        Assert.Equal(123, maxCompletionTokens.GetInt32());
        Assert.False(rewrittenBody.RootElement.TryGetProperty("max_tokens", out _));
    }
}
