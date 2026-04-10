using System.Net;
using System.Text;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Route-level tests verifying that event availability is enforced end-to-end:
/// inactive events, expired events, future events, and active events all produce
/// the correct HTTP response through the full middleware pipeline.
/// </summary>
public class EventAvailabilityRouteTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public EventAvailabilityRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ActiveEvent_WithinTimeWindow_Returns200()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: true,
            startTimestamp: DateTime.UtcNow.AddHours(-1),
            endTimestamp: DateTime.UtcNow.AddHours(1));
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var response = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task InactiveEvent_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // Event is within time window but Active = false
        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: false,
            startTimestamp: DateTime.UtcNow.AddHours(-1),
            endTimestamp: DateTime.UtcNow.AddHours(1));
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var response = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ExpiredEvent_PastEndTime_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // Event ended 2 hours ago
        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: true,
            startTimestamp: DateTime.UtcNow.AddHours(-3),
            endTimestamp: DateTime.UtcNow.AddHours(-2));
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var response = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task FutureEvent_BeforeStartTime_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // Event starts in 2 hours
        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: true,
            startTimestamp: DateTime.UtcNow.AddHours(2),
            endTimestamp: DateTime.UtcNow.AddHours(4));
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var response = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task ReactivatedEvent_BecomesAccessibleAgain()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        // Start with inactive event
        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: false);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString());
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var firstResp = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));
        Assert.Equal(HttpStatusCode.Unauthorized, firstResp.StatusCode);

        // Re-activate the event (upsert overwrites) and flush cache
        await _fixture.SeedEventAsync(eventId, "owner",
            catalogIds: catalogId,
            active: true);
        await _fixture.InvalidateCacheAsync();

        var secondResp = await _fixture.Client.SendAsync(CreateChatRequest(apiKey));
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
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
