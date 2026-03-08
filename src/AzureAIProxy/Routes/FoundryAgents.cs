using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Routes.CustomResults;
using AzureAIProxy.Services;
using AzureAIProxy.Models;

namespace AzureAIProxy.Routes;

/// <summary>
/// Routes for Azure AI Foundry Agent Service - proxies both:
///   1. Agent Definitions API: /agents, /agents/{name}/versions, etc. (uses api-version query param)
///   2. Assistants-compatible API: /assistants, /threads/{id}/runs, etc. (used by azure-ai-agents SDK)
/// The SDK sets its endpoint to {proxy}/api/v1 and sends requests to /assistants, /threads, etc.
/// </summary>
public static class FoundryAgents
{
    /// <summary>
    /// Maps routes for the Agent Definitions API under "/agents".
    /// </summary>
    public static RouteGroupBuilder MapFoundryAgentRoutes(this RouteGroupBuilder builder)
    {
        var agentsGroup = builder.MapGroup("/agents");

        // Root /agents endpoint
        agentsGroup.MapGet("", HandleAgentRequestAsync);
        agentsGroup.MapPost("", HandleAgentRequestAsync);

        // Catch-all for any sub-path under /agents/
        // Covers: /agents/{name}, /agents/{name}/versions, /agents/{name}/versions/{ver}, etc.
        agentsGroup.MapGet("/{**catchAll}", HandleAgentRequestAsync);
        agentsGroup.MapPost("/{**catchAll}", HandleAgentRequestAsync);
        agentsGroup.MapDelete("/{**catchAll}", HandleAgentRequestAsync);

        return builder;
    }

    /// <summary>
    /// Maps routes for the Assistants-compatible API used by the azure-ai-agents SDK.
    /// The SDK sends requests to /assistants, /threads, etc. at the project root.
    /// </summary>
    public static RouteGroupBuilder MapFoundryAssistantsRoutes(this RouteGroupBuilder builder)
    {
        // /assistants (create, list)
        builder.MapGet("/assistants", HandleAgentRequestAsync);
        builder.MapPost("/assistants", HandleAgentRequestAsync);
        // /assistants/{**path} (get, update, delete, sub-resources)
        builder.MapGet("/assistants/{**catchAll}", HandleAgentRequestAsync);
        builder.MapPost("/assistants/{**catchAll}", HandleAgentRequestAsync);
        builder.MapDelete("/assistants/{**catchAll}", HandleAgentRequestAsync);

        // /threads (create)
        builder.MapPost("/threads", HandleAgentRequestAsync);
        // /threads/{**path} (get, messages, runs, etc.)
        builder.MapGet("/threads/{**catchAll}", HandleAgentRequestAsync);
        builder.MapPost("/threads/{**catchAll}", HandleAgentRequestAsync);
        builder.MapDelete("/threads/{**catchAll}", HandleAgentRequestAsync);

        // /files (upload, list)
        builder.MapGet("/files", HandleAgentRequestAsync);
        builder.MapPost("/files", HandleAgentRequestAsync);
        // /files/{**path} (get, delete)
        builder.MapGet("/files/{**catchAll}", HandleAgentRequestAsync);
        builder.MapDelete("/files/{**catchAll}", HandleAgentRequestAsync);

        return builder;
    }

    [ApiKeyAuthorize]
    private static async Task<IResult> HandleAgentRequestAsync(
        [FromServices] ICatalogService catalogService,
        [FromServices] IProxyService proxyService,
        [FromServices] IFoundryAgentService foundryAgentService,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context
    )
    {
        var logger = loggerFactory.CreateLogger("FoundryAgents");
        logger.LogInformation("Foundry Agents handler invoked: {Method} {Path}", context.Request.Method, context.Request.Path);

        string requestPath = (string)context.Items["requestPath"]!;
        RequestContext requestContext = (RequestContext)context.Items["RequestContext"]!;
        JsonDocument requestJsonDoc = (JsonDocument)context.Items["jsonDoc"]!;

        logger.LogInformation("Foundry Agents: eventId={EventId}, requestPath={RequestPath}",
            requestContext.EventId, requestPath);

        // Validate that the caller owns any object they're trying to access
        var accessCheck = await ValidateObjectAccess(foundryAgentService, context.Request.Method, requestPath, requestContext);
        if (accessCheck is not null)
            return accessCheck;

        var deployment = await catalogService.GetEventFoundryAgentAsync(requestContext.EventId);
        if (deployment is null)
        {
            logger.LogWarning("Foundry Agents: No Foundry Agent deployment found for eventId={EventId}", requestContext.EventId);
            return OpenAIResult.NotFound("No Foundry Agent deployment found for the event.");
        }
        logger.LogInformation("Foundry Agents: Found deployment={DeploymentName}, endpoint={Endpoint}, useMI={UseMI}, modelType={ModelType}",
            deployment.DeploymentName, deployment.EndpointUrl, deployment.UseManagedIdentity, deployment.ModelType);

        // The upstream endpoint URL is the Foundry project endpoint
        // e.g. https://<account>.services.ai.azure.com/api/projects/<project>
        // requestPath will be "agents/..." - append to existing path
        // The client provides api-version as a query param — we forward it as-is
        // (agent definitions API uses api-version=v1, SDK uses dated versions)
        var url = new UriBuilder(deployment.EndpointUrl.TrimEnd('/'));
        url.Path = url.Path.TrimEnd('/') + "/" + requestPath;
        logger.LogInformation("Foundry Agents: Forwarding to upstream URL={Url}", url.Uri);

        var authHeader = await proxyService.GetAuthenticationHeaderAsync(deployment);
        logger.LogInformation("Foundry Agents: Auth header type={AuthType}", authHeader.Key);
        List<RequestHeader> requestHeaders = [authHeader];

        var methodHandlers = new Dictionary<string, Func<Task<(string, int)>>>
        {
            [HttpMethod.Get.Method] = () => proxyService.HttpGetAsync(url, requestHeaders, context, requestContext, deployment),
            [HttpMethod.Post.Method] = () => proxyService.HttpPostAsync(url, requestHeaders, context, requestJsonDoc!, requestContext, deployment),
            [HttpMethod.Delete.Method] = () => proxyService.HttpDeleteAsync(url, requestHeaders, context, requestContext, deployment),
        };

        if (methodHandlers.TryGetValue(context.Request.Method, out var handler))
        {
            try
            {
                var (responseContent, statusCode) = await handler();
                logger.LogInformation("Foundry Agents: Upstream response statusCode={StatusCode}, contentLength={Length}",
                    statusCode, responseContent?.Length ?? 0);
                if (statusCode >= 400)
                    logger.LogWarning("Foundry Agents: Upstream error body: {Body}", responseContent?.Length > 2000 ? responseContent[..2000] : responseContent);

                // Track agent version objects for ownership validation
                await TrackAgentObjects(foundryAgentService, context, requestPath, requestContext, responseContent ?? string.Empty, statusCode);

                return new ProxyResult(responseContent ?? string.Empty, statusCode);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                return OpenAIResult.ServiceUnavailable("The request was canceled due to timeout. Inner exception: " + ex.InnerException.Message);
            }
            catch (TaskCanceledException ex)
            {
                return OpenAIResult.ServiceUnavailable("The request was canceled: " + ex.Message);
            }
            catch (HttpRequestException ex)
            {
                return OpenAIResult.ServiceUnavailable("The request failed: " + ex.Message);
            }
            catch (Exception ex)
            {
                return OpenAIResult.InternalServerError($"An error occurred processing the request: {ex.Message}");
            }
        }
        return OpenAIResult.MethodNotAllowed("Unsupported HTTP method: " + context.Request.Method);
    }

    /// <summary>
    /// Validate that the caller owns the agent/assistant/thread they're trying to access.
    /// Only validates on operations targeting specific IDs (not list/create).
    /// </summary>
    private static async Task<IResult?> ValidateObjectAccess(
        IFoundryAgentService foundryAgentService,
        string method,
        string requestPath,
        RequestContext requestContext)
    {
        var segments = requestPath.Trim('/').Split('/');

        // Assistants SDK: GET/DELETE/POST /assistants/{id}[/...]
        if (segments.Length >= 2 && segments[0] == "assistants" && segments[1].StartsWith("asst_"))
        {
            var assistantId = segments[1];
            if (!await foundryAgentService.ValidateObjectAsync(requestContext.ApiKey, $"assistant:{assistantId}"))
                return OpenAIResult.Unauthorized("Unauthorized access to assistant.");
        }

        // Assistants SDK: GET/POST/DELETE /threads/{id}[/...]
        if (segments.Length >= 2 && segments[0] == "threads" && segments[1].StartsWith("thread_"))
        {
            var threadId = segments[1];
            if (!await foundryAgentService.ValidateObjectAsync(requestContext.ApiKey, $"thread:{threadId}"))
                return OpenAIResult.Unauthorized("Unauthorized access to thread.");
        }

        // Agent Definitions API: /agents/{name}/versions/{version}
        if (segments.Length >= 4 && segments[0] == "agents" && segments[2] == "versions")
        {
            var name = segments[1];
            var version = segments[3];
            if (!await foundryAgentService.ValidateObjectAsync(requestContext.ApiKey, $"agent:{name}:{version}"))
                return OpenAIResult.Unauthorized("Unauthorized access to agent version.");
        }

        // Files API: GET/DELETE /files/{id}[/...]
        if (segments.Length >= 2 && segments[0] == "files" && segments[1].StartsWith("file-"))
        {
            var fileId = segments[1];
            if (!await foundryAgentService.ValidateObjectAsync(requestContext.ApiKey, $"file:{fileId}"))
                return OpenAIResult.Unauthorized("Unauthorized access to file.");
        }

        return null;
    }

    /// <summary>
    /// Track agent/thread/assistant/file creation and deletion for ownership validation.
    /// For any successful POST response, automatically tracks known ID patterns
    /// (thread_*, asst_*, file-*) found in the response body. This handles composite
    /// operations like create_thread_and_process_run which create objects implicitly.
    /// </summary>
    private static async Task TrackAgentObjects(
        IFoundryAgentService foundryAgentService,
        HttpContext context,
        string requestPath,
        RequestContext requestContext,
        string responseContent,
        int statusCode)
    {
        if (statusCode < 200 || statusCode >= 300 || string.IsNullOrEmpty(responseContent))
            return;

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            var isPost = context.Request.Method == HttpMethod.Post.Method;
            var isDelete = context.Request.Method == HttpMethod.Delete.Method;

            if (isPost)
            {
                // Agent definitions API: POST /agents/{name}/versions
                if (requestPath.Contains("/versions"))
                {
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var version = root.TryGetProperty("version", out var verProp) ? verProp.GetString() : null;
                    if (name is not null && version is not null)
                        await foundryAgentService.AddObjectAsync(requestContext.ApiKey, $"agent:{name}:{version}", "agent");
                }

                // Auto-track any known object IDs in the response.
                // Handles both explicit creates and composite operations.
                await TrackKnownIds(foundryAgentService, root, requestContext.ApiKey);
            }
            else if (isDelete)
            {
                // Agent definitions API: DELETE /agents/{name}/versions/{version}
                if (requestPath.Contains("/versions/"))
                {
                    var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    var version = root.TryGetProperty("version", out var verProp) ? verProp.GetString() : null;
                    if (name is not null && version is not null)
                        await foundryAgentService.DeleteObjectAsync(requestContext.ApiKey, $"agent:{name}:{version}");
                }

                // Auto-delete tracked IDs found in the response
                await DeleteKnownIds(foundryAgentService, root, requestContext.ApiKey);
            }
        }
        catch (JsonException)
        {
            // Response wasn't valid JSON - skip tracking
        }
    }

    /// <summary>
    /// Scan a response JSON for known ID patterns and track them for ownership.
    /// Checks both "id" and "thread_id" fields at the top level.
    /// </summary>
    private static async Task TrackKnownIds(IFoundryAgentService foundryAgentService, JsonElement root, string apiKey)
    {
        // Track "id" field
        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        if (id is not null)
        {
            if (id.StartsWith("asst_"))
                await foundryAgentService.AddObjectAsync(apiKey, $"assistant:{id}", "assistant");
            else if (id.StartsWith("thread_"))
                await foundryAgentService.AddObjectAsync(apiKey, $"thread:{id}", "thread");
            else if (id.StartsWith("file-"))
                await foundryAgentService.AddObjectAsync(apiKey, $"file:{id}", "file");
        }

        // Track "thread_id" field (present in run responses from composite operations)
        var threadId = root.TryGetProperty("thread_id", out var threadIdProp) ? threadIdProp.GetString() : null;
        if (threadId is not null && threadId.StartsWith("thread_"))
            await foundryAgentService.AddObjectAsync(apiKey, $"thread:{threadId}", "thread");
    }

    /// <summary>
    /// Scan a response JSON for known ID patterns and remove ownership tracking.
    /// </summary>
    private static async Task DeleteKnownIds(IFoundryAgentService foundryAgentService, JsonElement root, string apiKey)
    {
        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        if (id is not null)
        {
            if (id.StartsWith("asst_"))
                await foundryAgentService.DeleteObjectAsync(apiKey, $"assistant:{id}");
            else if (id.StartsWith("thread_"))
                await foundryAgentService.DeleteObjectAsync(apiKey, $"thread:{id}");
            else if (id.StartsWith("file-"))
                await foundryAgentService.DeleteObjectAsync(apiKey, $"file:{id}");
        }
    }
}
