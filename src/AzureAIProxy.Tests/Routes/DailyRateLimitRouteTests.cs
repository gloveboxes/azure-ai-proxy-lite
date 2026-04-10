using System.Net;
using System.Text;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Verifies that the daily request rate limit is enforced end-to-end through
/// the real application pipeline. Seeds an event with a low DailyRequestCap
/// and confirms that requests are rejected with 429 once the cap is exceeded.
/// </summary>
public class DailyRateLimitRouteTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public DailyRateLimitRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Requests_Within_DailyCap_Succeed()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // DailyRequestCap = 5 — all requests below should succeed
        await _fixture.SeedEventAsync(eventId, "owner-limit", catalogIds: catalogId, dailyRequestCap: 5);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-limit", eventId);

        for (int i = 0; i < 3; i++)
        {
            var request = CreateChatRequest(apiKey);
            var response = await _fixture.Client.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Request {i + 1} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [SkippableFact]
    public async Task Requests_Exceeding_DailyCap_Return429()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // DailyRequestCap = 1.
        // The RateLimiterHandler checks `requestCount > DailyRequestCap` BEFORE the
        // request executes, and IncrementUsage runs AFTER the response. So:
        //   Request 1: count=0 (0 > 1 false → passes), then increments to 1
        //   Request 2: count=1 (1 > 1 false → passes), then increments to 2
        //   Request 3: count=2 (2 > 1 true  → 429)
        await _fixture.SeedEventAsync(eventId, "owner-limit", catalogIds: catalogId, dailyRequestCap: 1);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-limit", eventId);

        // First two requests succeed (count goes 0→1→2)
        for (int i = 0; i < 2; i++)
        {
            var request = CreateChatRequest(apiKey);
            var response = await _fixture.Client.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Request {i + 1} should have succeeded but got {(int)response.StatusCode}");
        }

        // Third request sees count=2 > cap=1 → 429
        var blockedRequest = CreateChatRequest(apiKey);
        var blockedResponse = await _fixture.Client.SendAsync(blockedRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, blockedResponse.StatusCode);
        var body = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Contains("daily request rate", body);
    }

    [SkippableFact]
    public async Task DailyLimit_Is_PerApiKey_Not_Global()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // cap=1 → 2 requests succeed, 3rd blocked (see timing notes above)
        await _fixture.SeedEventAsync(eventId, "owner-limit", catalogIds: catalogId, dailyRequestCap: 1);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var keyA = await _fixture.SeedAttendeeAsync("user-a", eventId);
        var keyB = await _fixture.SeedAttendeeAsync("user-b", eventId);

        // Send 2 requests with key A to accumulate count past the cap
        for (int i = 0; i < 2; i++)
        {
            var resp = await _fixture.Client.SendAsync(CreateChatRequest(keyA));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Key A is now rate-limited (count=2 > cap=1)
        var limitedResp = await _fixture.Client.SendAsync(CreateChatRequest(keyA));
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResp.StatusCode);

        // Key B should still work — different attendee, separate counter
        var keyBResp = await _fixture.Client.SendAsync(CreateChatRequest(keyB));
        Assert.Equal(HttpStatusCode.OK, keyBResp.StatusCode);
    }

    private static HttpRequestMessage CreateChatRequest(string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/gpt-4o/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", apiKey);
        request.Content = new StringContent(
            "{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
            Encoding.UTF8, "application/json");
        return request;
    }
}
