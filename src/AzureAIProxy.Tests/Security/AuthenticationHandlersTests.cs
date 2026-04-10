using System.Text.Encodings.Web;
using AzureAIProxy.Middleware;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Tests.TestDoubles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AzureAIProxy.Tests.Security;

public class AuthenticationHandlersTests
{
    [Fact]
    public async Task ApiKeyAuthenticationHandler_MissingHeader_ReturnsNoResult()
    {
        var authorizeService = new StubAuthorizeService();
        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.ApiKeyScheme, null, typeof(ApiKeyAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
    }

    [Fact]
    public async Task ApiKeyAuthenticationHandler_EmptyHeader_ReturnsFailure()
    {
        var authorizeService = new StubAuthorizeService();
        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers["api-key"] = "";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.ApiKeyScheme, null, typeof(ApiKeyAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("API key is empty.", result.Failure?.Message);
    }

    [Fact]
    public async Task ApiKeyAuthenticationHandler_ValidHeader_SetsRequestContext()
    {
        var expectedContext = TestData.CreateRequestContext(apiKey: "valid-key");
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = key => Task.FromResult(key == "valid-key" ? expectedContext : null)
        };

        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers["api-key"] = "valid-key";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.ApiKeyScheme, null, typeof(ApiKeyAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Same(expectedContext, context.Items["RequestContext"]);
    }

    [Fact]
    public async Task ApiKeyAuthenticationHandler_UnknownKey_ReturnsFailure()
    {
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = _ => Task.FromResult<RequestContext?>(null)
        };

        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers["api-key"] = "unknown-but-well-formed-key";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.ApiKeyScheme, null, typeof(ApiKeyAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Authentication failed.", result.Failure?.Message);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_MissingHeader_ReturnsNoResult()
    {
        var authorizeService = new StubAuthorizeService();
        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.None);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_EmptyToken_ReturnsFailure()
    {
        var authorizeService = new StubAuthorizeService();
        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("API key is empty.", result.Failure?.Message);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_ValidToken_SetsRequestContext()
    {
        var expectedContext = TestData.CreateRequestContext(apiKey: "token-123");
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = key => Task.FromResult(key == "token-123" ? expectedContext : null)
        };

        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer token-123";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Same(expectedContext, context.Items["RequestContext"]);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_UnknownToken_ReturnsFailure()
    {
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = _ => Task.FromResult<RequestContext?>(null)
        };

        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer unknown-token";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Authentication failed.", result.Failure?.Message);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_NonBearerScheme_ExtractsLastSegmentAsKey()
    {
        // Production code does .Split(" ").Last() — any scheme prefix is accepted.
        // This test documents that behaviour so a future fix can tighten it.
        var expectedContext = TestData.CreateRequestContext(apiKey: "the-key");
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = key => Task.FromResult(key == "the-key" ? expectedContext : null)
        };

        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "NotBearer the-key";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        // Documents current (lenient) behaviour — succeeds because Split(" ").Last() == "the-key"
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task BearerTokenAuthenticationHandler_BearerNoSpace_UsesLiteralBearerAsKey()
    {
        // "Bearer" with no space — Split(" ").Last() returns "Bearer" itself as the key
        var authorizeService = new StubAuthorizeService
        {
            IsUserAuthorizedAsyncFunc = _ => Task.FromResult<RequestContext?>(null)
        };

        var handler = new BearerTokenAuthenticationHandler(
            new StaticOptionsMonitor<ProxyAuthenticationOptions>(new ProxyAuthenticationOptions()),
            authorizeService,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer";

        await handler.InitializeAsync(
            new AuthenticationScheme(ProxyAuthenticationOptions.BearerTokenScheme, null, typeof(BearerTokenAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        // "Bearer" (no space) → Split(" ").Last() == "Bearer" → goes to IsUserAuthorizedAsync → null → fail
        Assert.False(result.Succeeded);
        Assert.Equal("Authentication failed.", result.Failure?.Message);
    }
}
