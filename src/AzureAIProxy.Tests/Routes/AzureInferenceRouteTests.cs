using System.Net;
using System.Text;
using System.Text.Json;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

public class AzureInferenceRouteTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public AzureInferenceRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    // --- POST /api/v1/chat/completions (requires BearerToken auth) ---

    [SkippableFact]
    public async Task ChatCompletions_NoBearerToken_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var response = await _fixture.Client.PostAsync(
            "/api/v1/chat/completions",
            JsonContent("{\"model\":\"mistral-large\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ChatCompletions_InvalidBearerToken_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        request.Content = JsonContent("{\"model\":\"mistral-large\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ChatCompletions_ApiKeyHeader_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // AzureInference routes use [BearerTokenAuthorize], not [ApiKeyAuthorize].
        // Sending api-key header should NOT authenticate.
        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-test");
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent("{\"model\":\"mistral-large\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ChatCompletions_ValidBearer_DeploymentNotFound_Returns404()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        await _fixture.SeedEventAsync(eventId, "owner-test");
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent("{\"model\":\"nonexistent-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("nonexistent-model", body);
    }

    [SkippableFact]
    public async Task ChatCompletions_ValidBearer_ValidDeployment_Returns200()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();
        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "mistral-large", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent("{\"model\":\"mistral-large\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but got {(int)response.StatusCode}. Body: {body}");
        Assert.True(body.Contains("choices") || body.Contains("Upstream proxy"),
            $"Unexpected mock response body: {body}");
    }

    [SkippableFact]
    public async Task Embeddings_ValidBearer_Returns200()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();
        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "text-embedding-ada", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/embeddings");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent("{\"model\":\"text-embedding-ada\",\"input\":\"hello world\"}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task ChatCompletions_ExtraParametersHeader_IsForwarded()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // Verifies the route doesn't reject requests with extra-parameters header.
        // (The mock proxy doesn't validate upstream headers, but this proves the header
        // doesn't break the pipeline.)
        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();
        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "mistral-large", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("extra-parameters", "pass-through");
        request.Content = JsonContent("{\"model\":\"mistral-large\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but got {(int)response.StatusCode}. Body: {body}");
    }

    [SkippableFact]
    public async Task CrossEvent_BearerKey_Cannot_Access_Other_Event_Deployment()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventA = $"evt-a-{Guid.NewGuid():N}";
        var eventB = $"evt-b-{Guid.NewGuid():N}";
        var catalogA = Guid.NewGuid().ToString();
        var catalogB = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventA, "owner-alice", catalogIds: catalogA);
        await _fixture.SeedEventAsync(eventB, "owner-bob", catalogIds: catalogB);
        await _fixture.SeedCatalogAsync(catalogA, "model-a", ModelType.Foundry_Model.ToStorageString());
        await _fixture.SeedCatalogAsync(catalogB, "model-b", ModelType.Foundry_Model.ToStorageString());

        var keyA = await _fixture.SeedAttendeeAsync("user-alice", eventA);

        // Key A tries to use model-b (which belongs to event B)
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chat/completions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", keyA);
        request.Content = JsonContent("{\"model\":\"model-b\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("model-b", body);
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");
}
