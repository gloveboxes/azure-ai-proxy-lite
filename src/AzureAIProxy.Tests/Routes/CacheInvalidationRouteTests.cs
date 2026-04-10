using System.Net;
using AzureAIProxy.Tests.Fixtures;

namespace AzureAIProxy.Tests.Routes;

/// <summary>
/// Tests for the /internal/cache/invalidate endpoint as defined in Program.cs,
/// exercised through the real application pipeline (not a re-implementation).
/// </summary>
public class CacheInvalidationRouteTests : IClassFixture<ProxyAppFixture>
{
    private const string EncryptionKey = "dev-encryption-key-change-in-production";

    private readonly ProxyAppFixture _fixture;

    public CacheInvalidationRouteTests(ProxyAppFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task CacheInvalidate_MissingHeader_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var response = await _fixture.Client.PostAsync("/internal/cache/invalidate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task CacheInvalidate_WrongKey_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", "wrong-key");

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task CacheInvalidate_CorrectKey_Returns200()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", EncryptionKey);

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    public async Task CacheInvalidate_CaseSensitive_Returns401()
    {
        Skip.IfNot(_fixture.Available, "Azurite not available");

        // The endpoint uses StringComparison.Ordinal — different casing must fail
        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", EncryptionKey.ToUpperInvariant());

        var response = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
