using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using AzureAIProxy.Services;

namespace AzureAIProxy.Middleware;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ProxyAuthenticationOptions> options,
    IAuthorizeService authorizeService,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<ProxyAuthenticationOptions>(options, logger, encoder)
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "api-key", "authorization", "cookie", "x-api-key" };

    private string RedactedHeaders() =>
        string.Join(", ", Request.Headers.Select(h =>
            $"{h.Key}: {(SensitiveHeaders.Contains(h.Key) ? "[REDACTED]" : h.Value.ToString())}"));

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("api-key", out var apiKeyValues))
            return AuthenticateResult.NoResult();

        var apiKey = apiKeyValues.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.LogWarning("api-key header present but empty. Received headers: {Headers}", RedactedHeaders());
            return AuthenticateResult.Fail("API key is empty.");
        }

        var requestContext = await authorizeService.IsUserAuthorizedAsync(apiKey);
        if (requestContext is null)
            return AuthenticateResult.Fail("Authentication failed.");

        Context.Items["RequestContext"] = requestContext;

        var identity = new ClaimsIdentity(null, nameof(ApiKeyAuthenticationHandler));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
