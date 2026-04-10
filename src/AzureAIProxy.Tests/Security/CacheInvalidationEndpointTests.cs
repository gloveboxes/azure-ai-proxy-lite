using System.Net;
using AzureAIProxy.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AzureAIProxy.Tests.Security;

/// <summary>
/// Tests for the /internal/cache/invalidate endpoint security gate.
/// The endpoint is a Minimal API lambda in Program.cs — we recreate its
/// exact logic here against a lightweight test server so the auth check
/// is exercised without needing full storage/proxy wiring.
/// </summary>
public class CacheInvalidationEndpointTests
{
    private const string EncryptionKey = "test-secret-key-12345";

    private static async Task<TestServer> CreateServer(string? encryptionKey = EncryptionKey)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(encryptionKey is not null
                ? new Dictionary<string, string?> { ["EncryptionKey"] = encryptionKey }
                : [])
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton<ICatalogCacheService, CatalogCacheService>();
        builder.Services.AddSingleton<IEventCacheService, EventCacheService>();

        var app = builder.Build();

        // Mirror the exact endpoint from Program.cs
        app.MapPost("/internal/cache/invalidate", (
            ICatalogCacheService catalogCache,
            IEventCacheService eventCache,
            IConfiguration cfg,
            HttpContext context) =>
        {
            var expectedKey = cfg["EncryptionKey"] ?? cfg["PostgresEncryptionKey"] ?? "";
            if (string.IsNullOrEmpty(expectedKey))
                return Results.StatusCode(503);

            if (!context.Request.Headers.TryGetValue("X-Cache-Key", out var keyValues)
                || !string.Equals(keyValues.ToString(), expectedKey, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            catalogCache.InvalidateAll();
            eventCache.InvalidateAll();
            return Results.Ok();
        });

        await app.StartAsync();
        return app.GetTestServer();
    }

    [Fact]
    public async Task CacheInvalidate_MissingHeader_Returns401()
    {
        using var server = await CreateServer();
        var client = server.CreateClient();

        var response = await client.PostAsync("/internal/cache/invalidate", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CacheInvalidate_WrongKey_Returns401()
    {
        using var server = await CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", "wrong-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CacheInvalidate_CorrectKey_Returns200()
    {
        using var server = await CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", EncryptionKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CacheInvalidate_NoEncryptionKeyConfigured_Returns503()
    {
        using var server = await CreateServer(encryptionKey: null);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", "anything");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CacheInvalidate_CaseSensitiveComparison()
    {
        using var server = await CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/internal/cache/invalidate");
        request.Headers.Add("X-Cache-Key", EncryptionKey.ToUpperInvariant());

        var response = await client.SendAsync(request);

        // Key comparison is Ordinal — case mismatch must be rejected
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
