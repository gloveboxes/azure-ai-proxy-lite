using System.Security.Claims;
using Azure;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace AzureAIProxy.Management.Services;

public class AuthService(AuthenticationStateProvider authenticationStateProvider, ITableStorageService tableStorage) : IAuthService
{
    private static string? ResolveUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity?.Name;
    }

    public async Task<string> GetCurrentUserIdAsync()
    {
        AuthenticationState authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("User is not authenticated");

        string userId = ResolveUserId(authState.User)
            ?? throw new UnauthorizedAccessException("User ID claim not found");
        return userId;
    }

    public async Task<(string email, string name)> GetCurrentUserEmailNameAsync()
    {
        string userId = await GetCurrentUserIdAsync();
        var ownerTable = tableStorage.GetTableClient(TableNames.Owners);

        try
        {
            var response = await ownerTable.GetEntityAsync<OwnerEntity>("owner", userId);
            return (response.Value.Email, response.Value.Name);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (string.Empty, string.Empty);
        }
    }
}
