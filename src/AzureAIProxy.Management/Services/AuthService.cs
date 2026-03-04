using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AzureAIProxy.Management.Services;

public class AuthService(AuthenticationStateProvider authenticationStateProvider, IDbContextFactory<AzureAIProxyDbContext> dbFactory) : IAuthService
{
    public async Task<string> GetCurrentUserIdAsync()
    {
        AuthenticationState authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        string userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new ApplicationException("User ID claim not found");
        return userId;
    }

    public async Task<(string email, string name)> GetCurrentUserEmailNameAsync()
    {
        string userId = await GetCurrentUserIdAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var owner = await db.Owners
                             .Where(o => o.OwnerId == userId)
                             .Select(o => new { o.Name, o.Email })
                             .FirstOrDefaultAsync();

        return (owner?.Email ?? string.Empty, owner?.Name ?? string.Empty);
    }
}
