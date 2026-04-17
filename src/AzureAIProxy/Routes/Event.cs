using Microsoft.AspNetCore.Mvc;
using AzureAIProxy.Models;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;
using Microsoft.AspNetCore.Authorization;

namespace AzureAIProxy.Routes;

public static class Event
{
    public static RouteGroupBuilder MapEventRoutes(this RouteGroupBuilder builder)
    {
        builder.MapPost("/eventinfo", EventInfoAsync);
        builder.MapGet("/event/{eventId}", EventRegistrationInfoAsync);
        return builder;
    }

    [ApiKeyAuthorize]
    private static async Task<IResult> EventInfoAsync(
        [FromServices] ICatalogService catalogService,
        HttpContext context
    )
    {
        RequestContext requestContext = (RequestContext)context.Items["RequestContext"]!;
        var capabilities = await catalogService.GetCapabilitiesAsync(requestContext.EventId);

        var eventInfo = new EventInfoResponse
        {
            IsAuthorized = requestContext.IsAuthorized,
            MaxTokenCap = requestContext.MaxTokenCap,
            EventCode = requestContext.EventCode,
            OrganizerName = requestContext.OrganizerName,
            OrganizerEmail = requestContext.OrganizerEmail,
            Capabilities = capabilities
        };

        return TypedResults.Ok(eventInfo);
    }

    [AllowAnonymous]
    private static async Task<IResult> EventRegistrationInfoAsync(
        [FromServices] IEventService eventService,
        [FromServices] ICatalogService catalogService,
        [FromServices] IConfiguration configuration,
        HttpContext context,
        string eventId
    )
    {
        var eventRegistrationInfo = await eventService.GetEventRegistrationInfoAsync(eventId);

        if (eventRegistrationInfo is null)
            return TypedResults.NotFound("Event not found.");

        // Resolve proxy base URL: explicit config > CONTAINER_APP_HOSTNAME > request host
        var proxyUrl = GetProxyBaseUrl(configuration, context);
        eventRegistrationInfo.ProxyUrl = proxyUrl;

        // Get all capabilities (model type → deployment names) for this event
        var capabilities = await catalogService.GetCapabilitiesAsync(eventId);

        // Get Foundry Toolkit deployments for this event
        var foundryToolkitDeployments = await catalogService.GetFoundryToolkitDeploymentsAsync(eventId);
        if (foundryToolkitDeployments.Count > 0 && proxyUrl is not null)
        {
            eventRegistrationInfo.FoundryToolkitEndpoints = foundryToolkitDeployments
                .Select(name => new FoundryToolkitEndpoint
                {
                    DeploymentName = name,
                    EndpointUrl = $"{proxyUrl}/openai/deployments/{name}/chat/completions"
                })
                .ToList();
        }

        // Publish proxied MCP server endpoints for this event
        if (
            proxyUrl is not null
            && capabilities.TryGetValue(ModelType.MCP_Server.ToStorageString(), out var mcpDeployments)
            && mcpDeployments.Count > 0
        )
        {
            eventRegistrationInfo.McpServerEndpoints = mcpDeployments
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new McpServerEndpoint
                {
                    DeploymentName = name,
                    EndpointUrl = $"{proxyUrl}/mcp/{Uri.EscapeDataString(name)}"
                })
                .ToList();
        }

        if (capabilities.Count > 0)
        {
            eventRegistrationInfo.Capabilities = capabilities;
        }

        return TypedResults.Ok(eventRegistrationInfo);
    }

    private static string? GetProxyBaseUrl(IConfiguration configuration, HttpContext context)
    {
        // 1. Explicit configuration (highest priority)
        var configuredUrl = configuration["ProxyUrl"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
            return configuredUrl.TrimEnd('/');

        // 2. Use the request host, which reflects the stable app FQDN
        //    (CONTAINER_APP_HOSTNAME is revision-scoped and changes on every deploy)
        return $"{context.Request.Scheme}://{context.Request.Host}/api/v1";
    }
}
