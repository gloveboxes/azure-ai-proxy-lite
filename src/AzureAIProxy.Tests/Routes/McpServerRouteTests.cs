using System.Net;
using System.Net.Sockets;
using System.Text;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Integration tests for the MCP Server pass-through proxy routes.
/// Exercises authentication, deployment lookup, query parameter encoding,
/// and upstream forwarding via the mock proxy.
/// </summary>
public class McpServerRouteTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;
    private const string EventId = "mcp-test1";
    private const string OwnerId = "owner-mcp";
    private const string McpCatalogId = "c0000000-0000-0000-0000-0000000000c1";
    private const string McpDeploymentName = "my-mcp-server";

    public McpServerRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> SeedMcpEventAsync()
    {
        await _fixture.SeedCatalogAsync(McpCatalogId, McpDeploymentName, ModelType.MCP_Server.ToStorageString());
        await _fixture.SeedEventAsync(EventId, OwnerId, catalogIds: McpCatalogId);
        var apiKey = await _fixture.SeedAttendeeAsync("mcp-user", EventId);
        await _fixture.InvalidateCacheAsync();
        return apiKey;
    }

    [SkippableFact]
    public async Task McpRoute_WithoutApiKey_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var response = await _fixture.Client.PostAsync(
            $"/api/v1/mcp/{McpDeploymentName}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task McpRoute_WithInvalidApiKey_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/mcp/{McpDeploymentName}");
        request.Headers.Add("api-key", "invalid-key");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task McpRoute_NonExistentDeployment_Returns404()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var apiKey = await SeedMcpEventAsync();

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/mcp/nonexistent-server");
        request.Headers.Add("api-key", apiKey);

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task McpRoute_QueryParams_AreUrlEncoded()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var apiKey = await SeedMcpEventAsync();

        // Send query params with characters requiring URL encoding.
        // The MCP route builds the upstream URL with Uri.EscapeDataString;
        // the mock proxy can't connect upstream, so we expect a 502/503 rather
        // than 500 (which would indicate a malformed URL crashing the handler).
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/mcp/{McpDeploymentName}?api-version=2024-01-01&filter=a%26b");
        request.Headers.Add("api-key", apiKey);
        request.Content = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1}",
            System.Text.Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);

        // The handler catches HttpRequestException and returns 503 (ServiceUnavailable)
        // because the upstream fake-endpoint.example.com is unreachable.
        // The critical assertion: it does NOT return 500 (which would mean the
        // URL was malformed and caused an unhandled exception).
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [SkippableFact]
    public async Task McpRoute_ForwardsCustomHeaders_AndUsesDeploymentApiKey()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var upstreamPort = GetFreeTcpPort();
        var upstreamPrefix = $"http://127.0.0.1:{upstreamPort}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(upstreamPrefix);
        listener.Start();

        var receivedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var acceptHeader = string.Empty;

        var listenerTask = Task.Run(async () =>
        {
            var upstreamContext = await listener.GetContextAsync();
            foreach (var key in upstreamContext.Request.Headers.AllKeys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    receivedHeaders[key] = upstreamContext.Request.Headers[key]!;
                }
            }

            acceptHeader = upstreamContext.Request.Headers["Accept"] ?? string.Empty;

            var body = Encoding.UTF8.GetBytes("{\"ok\":true}");
            upstreamContext.Response.StatusCode = 200;
            upstreamContext.Response.ContentType = "application/json";
            await upstreamContext.Response.OutputStream.WriteAsync(body, 0, body.Length);
            upstreamContext.Response.Close();
        });

        var eventId = $"evt-mcp-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();
        var deploymentName = "header-proxy-test";
        const string attendeeApiKeyMarker = "attendee-api-key-marker";
        const string upstreamApiKey = "upstream-deployment-api-key";

        await _fixture.SeedCatalogAsync(
            catalogId,
            deploymentName,
            ModelType.MCP_Server.ToStorageString(),
            endpointUrl: upstreamPrefix.TrimEnd('/'),
            endpointKey: upstreamApiKey
        );
        await _fixture.SeedEventAsync(eventId, "owner-header-test", catalogIds: catalogId);
        var attendeeApiKey = await _fixture.SeedAttendeeAsync("user-header-test", eventId);
        await _fixture.InvalidateCacheAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/mcp/{deploymentName}");
        request.Headers.Add("api-key", attendeeApiKey);
        request.Headers.Add("x-custom-token", "custom-forward-me");
        request.Headers.Add("Mcp-Session-Id", "session-abc");
        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Content = new StringContent(
            $"{{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":\"{attendeeApiKeyMarker}\"}}",
            Encoding.UTF8,
            "application/json"
        );

        var response = await _fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var completed = await Task.WhenAny(listenerTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(listenerTask, completed);

        Assert.True(receivedHeaders.TryGetValue("x-custom-token", out var customHeader));
        Assert.Equal("custom-forward-me", customHeader);

        Assert.True(receivedHeaders.TryGetValue("Mcp-Session-Id", out var sessionHeader));
        Assert.Equal("session-abc", sessionHeader);

        Assert.Equal("application/json, text/event-stream", acceptHeader);

        Assert.True(receivedHeaders.TryGetValue("api-key", out var upstreamHeaderValue));
        Assert.Equal(upstreamApiKey, upstreamHeaderValue);
        Assert.NotEqual(attendeeApiKey, upstreamHeaderValue);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
