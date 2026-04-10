using System.Net;
using System.Text;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Tests.Fixtures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Integration;

/// <summary>
/// Verifies that setting a catalog entry's Active flag to false makes the
/// deployment invisible — both at the service layer and through the route pipeline.
/// </summary>
public class DisabledCatalogTests : IClassFixture<ProxyAppFixture>
{
    private readonly ProxyAppFixture _fixture;

    public DisabledCatalogTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task GetCatalogItem_InactiveCatalog_ReturnsNull()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString(), active: false);

        var catalogService = new CatalogService(
            _fixture.TableStorage,
            _fixture.Encryption,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheService(),
            NullLogger<CatalogService>.Instance);

        var (deployment, eventCatalog) = await catalogService.GetCatalogItemAsync(eventId, "gpt-4o");

        Assert.Null(deployment);
        Assert.DoesNotContain(eventCatalog, d => d.DeploymentName == "gpt-4o");
    }

    [SkippableFact]
    public async Task GetCatalogItem_ActiveCatalog_ReturnsDeployment()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "gpt-4o", ModelType.Foundry_Model.ToStorageString(), active: true);

        var catalogService = new CatalogService(
            _fixture.TableStorage,
            _fixture.Encryption,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheService(),
            NullLogger<CatalogService>.Instance);

        var (deployment, _) = await catalogService.GetCatalogItemAsync(eventId, "gpt-4o");

        Assert.NotNull(deployment);
        Assert.Equal("gpt-4o", deployment!.DeploymentName);
    }

    [SkippableFact]
    public async Task Route_InactiveCatalog_Returns404()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var catalogId = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventId, "owner-test", catalogIds: catalogId);
        await _fixture.SeedCatalogAsync(catalogId, "disabled-model", ModelType.Foundry_Model.ToStorageString(), active: false);
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/disabled-model/chat/completions?api-version=2024-10-21");
        request.Headers.Add("api-key", apiKey);
        request.Content = new StringContent(
            "{\"model\":\"disabled-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
            Encoding.UTF8, "application/json");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("disabled-model", body);
    }

    [SkippableFact]
    public async Task Route_MixedActiveInactive_OnlyActiveVisible()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var eventId = $"evt-{Guid.NewGuid():N}";
        var activeCatalogId = Guid.NewGuid().ToString();
        var inactiveCatalogId = Guid.NewGuid().ToString();

        await _fixture.SeedEventAsync(eventId, "owner-test",
            catalogIds: $"{activeCatalogId},{inactiveCatalogId}");
        await _fixture.SeedCatalogAsync(activeCatalogId, "active-model", ModelType.Foundry_Model.ToStorageString(), active: true);
        await _fixture.SeedCatalogAsync(inactiveCatalogId, "inactive-model", ModelType.Foundry_Model.ToStorageString(), active: false);
        var apiKey = await _fixture.SeedAttendeeAsync("user-1", eventId);

        // Active model should work
        var activeReq = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/active-model/chat/completions?api-version=2024-10-21");
        activeReq.Headers.Add("api-key", apiKey);
        activeReq.Content = new StringContent(
            "{\"model\":\"active-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
            Encoding.UTF8, "application/json");

        var activeResp = await _fixture.Client.SendAsync(activeReq);
        Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);

        // Inactive model should 404
        var inactiveReq = new HttpRequestMessage(HttpMethod.Post,
            "/api/v1/openai/deployments/inactive-model/chat/completions?api-version=2024-10-21");
        inactiveReq.Headers.Add("api-key", apiKey);
        inactiveReq.Content = new StringContent(
            "{\"model\":\"inactive-model\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}",
            Encoding.UTF8, "application/json");

        var inactiveResp = await _fixture.Client.SendAsync(inactiveReq);
        Assert.Equal(HttpStatusCode.NotFound, inactiveResp.StatusCode);
    }
}
