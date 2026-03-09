using Microsoft.AspNetCore.Mvc;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Routes.CustomResults;
using AzureAIProxy.Services;

namespace AzureAIProxy.Routes;

/// <summary>
/// Routes for MCP (Model Context Protocol) Server pass-through proxy.
/// Forwards MCP Streamable HTTP transport requests to upstream MCP servers.
/// Client URL: /api/v1/mcp/{deploymentName}/{path} → upstream: {endpointUrl}/{path}
/// </summary>
public static class McpServer
{
    private static readonly string[] ForwardedRequestHeaders =
        ["Accept", "Mcp-Session-Id", "Last-Event-ID"];

    private static readonly string[] ForwardedResponseHeaders =
        ["Mcp-Session-Id"];

    public static RouteGroupBuilder MapMcpServerRoutes(this RouteGroupBuilder builder)
    {
        var mcpGroup = builder.MapGroup("/mcp/{deploymentName}");

        mcpGroup.MapGet("/{**catchAll}", HandleMcpRequestAsync);
        mcpGroup.MapPost("/{**catchAll}", HandleMcpRequestAsync);
        mcpGroup.MapDelete("/{**catchAll}", HandleMcpRequestAsync);

        mcpGroup.MapGet("", HandleMcpRequestAsync);
        mcpGroup.MapPost("", HandleMcpRequestAsync);
        mcpGroup.MapDelete("", HandleMcpRequestAsync);

        return builder;
    }

    [ApiKeyAuthorize]
    private static async Task HandleMcpRequestAsync(
        [FromServices] ICatalogService catalogService,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext context,
        string deploymentName,
        string? catchAll = null
    )
    {
        var logger = loggerFactory.CreateLogger("McpServer");
        logger.LogInformation("MCP proxy: BEGIN {Method} {Path} deploymentName={DeploymentName} catchAll={CatchAll}",
            context.Request.Method, context.Request.Path, deploymentName, catchAll ?? "(none)");

        // Log incoming headers for diagnostics
        foreach (var header in context.Request.Headers)
        {
            if (!header.Key.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("MCP proxy: request header {Key}: {Value}", header.Key, header.Value.ToString());
            }
        }

        RequestContext requestContext = (RequestContext)context.Items["RequestContext"]!;
        logger.LogInformation("MCP proxy: authenticated eventId={EventId}", requestContext.EventId);

        var deployment = await catalogService.GetEventMcpServerAsync(requestContext.EventId, deploymentName);
        if (deployment is null)
        {
            logger.LogWarning("MCP proxy: deployment '{DeploymentName}' NOT FOUND for event {EventId}", deploymentName, requestContext.EventId);
            await OpenAIResult.NotFound(
                $"MCP Server deployment '{deploymentName}' not found for this event."
            ).ExecuteAsync(context);
            return;
        }

        logger.LogInformation("MCP proxy: found deployment endpointUrl={EndpointUrl} endpointKey={HasKey}",
            deployment.EndpointUrl, !string.IsNullOrEmpty(deployment.EndpointKey) ? "yes" : "no");

        // Build upstream URL: endpointUrl + catchAll path
        var upstreamUrl = new UriBuilder(deployment.EndpointUrl.TrimEnd('/'));
        if (!string.IsNullOrEmpty(catchAll))
        {
            upstreamUrl.Path = upstreamUrl.Path.TrimEnd('/') + "/" + catchAll;
        }

        // Append query parameters from the incoming request
        var queryParams = context.Request.Query
            .Where(q => !string.IsNullOrEmpty(q.Value))
            .Select(q => $"{q.Key}={q.Value!}")
            .ToList();

        if (!string.IsNullOrEmpty(upstreamUrl.Query))
        {
            var existing = upstreamUrl.Query.TrimStart('?');
            if (!string.IsNullOrEmpty(existing))
                queryParams.Insert(0, existing);
        }
        upstreamUrl.Query = string.Join("&", queryParams);

        logger.LogInformation("MCP proxy: upstream URL = {Url}", upstreamUrl.Uri);

        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(120);

        using var requestMessage = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            upstreamUrl.Uri
        );

        // Forward MCP-relevant request headers
        foreach (var headerName in ForwardedRequestHeaders)
        {
            if (context.Request.Headers.TryGetValue(headerName, out var values))
            {
                requestMessage.Headers.TryAddWithoutValidation(headerName, values.ToArray());
                logger.LogInformation("MCP proxy: forwarding header {Key}: {Value}", headerName, values.ToString());
            }
        }

        // Forward endpoint key to upstream MCP server if configured
        if (!string.IsNullOrEmpty(deployment.EndpointKey))
        {
            requestMessage.Headers.TryAddWithoutValidation("api-key", deployment.EndpointKey);
            logger.LogInformation("MCP proxy: forwarding api-key header to upstream");
        }

        // Forward request body for POST (use JSON parsed by LoadProperties middleware)
        if (context.Request.Method == HttpMethod.Post.Method)
        {
            var jsonDoc = context.Items["jsonDoc"] as System.Text.Json.JsonDocument;
            if (jsonDoc is not null)
            {
                var bodyJson = jsonDoc.RootElement.ToString();
                logger.LogInformation("MCP proxy: forwarding POST body ({Length} chars): {Body}",
                    bodyJson.Length, bodyJson.Length <= 500 ? bodyJson : bodyJson[..500] + "...");
                requestMessage.Content = new StringContent(
                    bodyJson,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
            }
            else
            {
                logger.LogWarning("MCP proxy: POST request but jsonDoc is null (no body parsed by middleware)");
            }
        }

        logger.LogInformation("MCP proxy: sending {Method} to upstream...", context.Request.Method);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted
            );
            logger.LogInformation("MCP proxy: upstream responded {StatusCode} {ReasonPhrase}",
                (int)response.StatusCode, response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("MCP proxy: client disconnected before upstream responded");
            return;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "MCP proxy: upstream request TIMED OUT after 120s");
            await OpenAIResult.ServiceUnavailable("MCP request timed out: " + ex.Message)
                .ExecuteAsync(context);
            return;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "MCP proxy: upstream request FAILED: {Message}", ex.Message);
            await OpenAIResult.ServiceUnavailable("MCP request failed: " + ex.Message)
                .ExecuteAsync(context);
            return;
        }

        using (response)
        {
            // Set response status and headers before streaming body
            context.Response.StatusCode = (int)response.StatusCode;

            if (response.Content.Headers.ContentType is not null)
            {
                context.Response.ContentType = response.Content.Headers.ContentType.ToString();
                logger.LogInformation("MCP proxy: response Content-Type: {ContentType}", response.Content.Headers.ContentType);
            }

            // Log all upstream response headers for diag
            foreach (var h in response.Headers)
            {
                logger.LogInformation("MCP proxy: response header {Key}: {Value}", h.Key, string.Join(", ", h.Value));
            }

            foreach (var headerName in ForwardedResponseHeaders)
            {
                if (response.Headers.TryGetValues(headerName, out var values))
                {
                    context.Response.Headers[headerName] = values.ToArray();
                }
            }

            // Stream the response body with flush-per-chunk for SSE support
            long totalBytes = 0;
            try
            {
                await using var responseStream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await responseStream.ReadAsync(buffer, context.RequestAborted)) > 0)
                {
                    totalBytes += bytesRead;
                    await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead), context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
                logger.LogInformation("MCP proxy: finished streaming {TotalBytes} bytes to client", totalBytes);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning("MCP proxy: client disconnected during streaming after {TotalBytes} bytes", totalBytes);
            }
        }

        logger.LogInformation("MCP proxy: END {Method} {Path}", context.Request.Method, context.Request.Path);
    }
}
