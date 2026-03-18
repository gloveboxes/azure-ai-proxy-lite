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
            EventImageUrl = requestContext.EventImageUrl,
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

        // Get AI Toolkit deployments for this event
        var aiToolkitDeployments = await catalogService.GetAiToolkitDeploymentsAsync(eventId);
        if (aiToolkitDeployments.Count > 0 && proxyUrl is not null)
        {
            eventRegistrationInfo.AiToolkitEndpoints = aiToolkitDeployments
                .Select(name => new AiToolkitEndpoint
                {
                    DeploymentName = name,
                    EndpointUrl = $"{proxyUrl}/openai/deployments/{name}/chat/completions?api-version=2025-01-01-preview"
                })
                .ToList();
        }

        return TypedResults.Ok(eventRegistrationInfo);
    }

    private static string? GetProxyBaseUrl(IConfiguration configuration, HttpContext context)
    {
        // 1. Explicit configuration (highest priority)
        var configuredUrl = configuration["ProxyUrl"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
            return configuredUrl.TrimEnd('/');

        // 2. Azure Container Apps provides CONTAINER_APP_HOSTNAME at runtime
        var containerHostname = Environment.GetEnvironmentVariable("CONTAINER_APP_HOSTNAME");
        if (!string.IsNullOrWhiteSpace(containerHostname))
            return $"https://{containerHostname}/api/v1";

        // 3. Fall back to the current request (works for local dev and direct access)
        return $"{context.Request.Scheme}://{context.Request.Host}/api/v1";
    }
}
