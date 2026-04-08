using System.Security.Claims;
using Azure;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Admin.Middleware;

/// <summary>
/// Ensures an Owner record exists in Table Storage for the authenticated user.
/// Runs on each authenticated request but only writes on first login (404 check).
/// </summary>
public class EnsureOwnerMiddleware(RequestDelegate next)
{
    private static string? ResolveUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity?.Name;
    }

    public async Task InvokeAsync(HttpContext context, ITableStorageService tableStorage)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = ResolveUserId(context.User);
            var name = context.User.FindFirst(ClaimTypes.Name)?.Value
                ?? context.User.FindFirst("name")?.Value
                ?? userId;
            var email = context.User.FindFirst(ClaimTypes.Email)?.Value
                ?? context.User.FindFirst("preferred_username")?.Value
                ?? $"{userId}@admin";

            if (!string.IsNullOrEmpty(userId))
            {
                var ownerTable = tableStorage.GetTableClient(TableNames.Owners);
                try
                {
                    await ownerTable.GetEntityAsync<OwnerEntity>("owner", userId);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    try
                    {
                        await ownerTable.AddEntityAsync(new OwnerEntity
                        {
                            PartitionKey = "owner",
                            RowKey = userId,
                            Name = name ?? userId,
                            Email = email
                        });
                    }
                    catch (RequestFailedException addEx) when (addEx.Status == 409)
                    {
                        // Another request already created the owner — no-op
                    }
                }
            }
        }

        await next(context);
    }
}
