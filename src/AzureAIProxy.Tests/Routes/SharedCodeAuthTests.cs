using System.Net;
using System.Text;
using System.Text.Json;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Tests the shared-code authentication feature boundaries.
/// NOTE: Full shared-code end-to-end cannot be tested through the route pipeline because
/// shared-code API keys contain '/' which is invalid in Azure Table Storage row keys.
/// Azurite returns 500 instead of 404 for these keys, preventing the shared-code
/// fallback from triggering. These tests validate adjacent behaviors.
/// </summary>
public class SharedCodeAuthTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public SharedCodeAuthTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    [SkippableFact]
    public async Task RegularApiKey_StillWorks_WhenEventHasSharedCode()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // Event with a shared code set — normal API key still works
        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();
        await _fixture.SeedEventAsync(eventId, "owner-1", catalogIds: catalogId, eventSharedCode: "SHARE");
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/gpt-4o/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent("{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task PlainInvalidKey_Returns401_Not500()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // A regular invalid key (no special chars) should return 401 cleanly
        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/gpt-4o/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", "completely-invalid-regular-key");
        request.Content = JsonContent("{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ShortMalformedApiKey_Returns401_Not500()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/gpt-4o/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", "x");
        request.Content = JsonContent("{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
