using AzureAIProxy.Services;
using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Middleware;

public class RateLimiterHandler(RequestDelegate next, IRateLimitService rateLimitService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        RequestContext? requestContext = context.Items["RequestContext"] as RequestContext;

        if (requestContext is not null)
        {
            var requestCount = rateLimitService.GetRequestCount(requestContext.ApiKey);
            if (requestCount >= requestContext.DailyRequestCap)
            {
                await OpenAIErrorResponse.TooManyRequests(
                    $"The event daily request rate of {requestContext.DailyRequestCap} calls has been exceeded. Requests are disabled until UTC midnight."
                ).WriteAsync(context);
                return;
            }
        }

        await next(context);
    }
}
