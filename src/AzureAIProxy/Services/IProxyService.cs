using System.Text.Json;
using AzureAIProxy.Models;
using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Services;

public interface IProxyService
{
    Task<RequestHeader> GetAuthenticationHeaderAsync(Deployment deployment, bool useBearerToken = false);

    Task<(string responseContent, int statusCode)> HttpPostAsync(
        UriBuilder requestUrl,
        List<RequestHeader> requestHeaders,
        HttpContext context,
        JsonDocument requestJsonDoc,
        RequestContext requestContext,
        Deployment deployment
    );
    Task HttpPostStreamAsync(
        UriBuilder requestUrl,
        List<RequestHeader> requestHeaders,
        HttpContext context,
        JsonDocument requestJsonDoc,
        RequestContext requestContext,
        Deployment deployment
    );
    Task<(string responseContent, int statusCode)> HttpGetAsync(
        UriBuilder requestUrl,
        List<RequestHeader> requestHeaders,
        HttpContext context,
        RequestContext requestContext,
        Deployment deployment
    );
    Task<(string responseContent, int statusCode)> HttpDeleteAsync(
        UriBuilder requestUrl,
        List<RequestHeader> requestHeaders,
        HttpContext context,
        RequestContext requestContext,
        Deployment deployment
    );
    Task<(string responseContent, int statusCode)> HttpPostFormAsync(
      UriBuilder requestUrl,
      List<RequestHeader> requestHeaders,
      HttpContext context,
      HttpRequest request,
      RequestContext requestContext,
      Deployment deployment
  );
}
