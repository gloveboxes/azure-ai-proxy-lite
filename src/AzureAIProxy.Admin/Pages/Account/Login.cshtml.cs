using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Admin.Pages.Account;

[AllowAnonymous]
public class LoginModel(IConfiguration configuration, ITableStorageService tableStorage) : PageModel
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, (int Count, DateTime LastAttempt)> _failedAttempts = new();

    public bool UseEntraAuth => !string.IsNullOrEmpty(configuration["AzureAd:ClientId"]);

    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (UseEntraAuth)
        {
            var redirectUri = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
            return Challenge(
                new AuthenticationProperties { RedirectUri = redirectUri },
                OpenIdConnectDefaults.AuthenticationScheme);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        if (UseEntraAuth)
            return RedirectToPage();

        var safeReturnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (IsLockedOut(clientIp))
        {
            ErrorMessage = "Too many failed login attempts. Please try again later.";
            return Page();
        }

        // Check against array of configured users first, then fall back to single Admin:Username/Password
        var users = configuration.GetSection("Admin:Users").GetChildren().ToList();
        bool authenticated = false;

        if (users.Count > 0)
        {
            foreach (var user in users)
            {
                var configUser = user["Username"];
                var configPass = user["Password"];
                if (!string.IsNullOrEmpty(configUser) && !string.IsNullOrEmpty(configPass) && FixedTimeEquals(username, configUser) && FixedTimeEquals(password, configPass))
                {
                    authenticated = true;
                    break;
                }
            }
        }
        else
        {
            var configUsername = configuration["Admin:Username"];
            var configPassword = configuration["Admin:Password"];

            if (string.IsNullOrEmpty(configUsername) || string.IsNullOrEmpty(configPassword))
            {
                ErrorMessage = "Admin credentials are not configured. Set Admin__Username and Admin__Password environment variables.";
                return Page();
            }

            authenticated = FixedTimeEquals(username, configUsername) && FixedTimeEquals(password, configPassword);
        }

        if (!authenticated)
        {
            RecordFailedAttempt(clientIp);
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

        ClearFailedAttempts(clientIp);

        // Ensure the owner record exists in Table Storage
        var ownerTable = tableStorage.GetTableClient(TableNames.Owners);
        try
        {
            await ownerTable.GetEntityAsync<OwnerEntity>("owner", username);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            try
            {
                await ownerTable.AddEntityAsync(new OwnerEntity
                {
                    PartitionKey = "owner",
                    RowKey = username,
                    Name = username,
                    Email = $"{username}@admin"
                });
            }
            catch (RequestFailedException addEx) when (addEx.Status == 409)
            {
                // Another request already created the owner — no-op
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Name, username),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return LocalRedirect(safeReturnUrl!);
    }

    private static bool IsLockedOut(string clientIp)
    {
        if (!_failedAttempts.TryGetValue(clientIp, out var entry))
            return false;

        if (entry.Count >= MaxFailedAttempts && DateTime.UtcNow - entry.LastAttempt < LockoutDuration)
            return true;

        if (entry.Count >= MaxFailedAttempts)
        {
            _failedAttempts.TryRemove(clientIp, out _);
            return false;
        }

        return false;
    }

    private static void RecordFailedAttempt(string clientIp)
    {
        _failedAttempts.AddOrUpdate(
            clientIp,
            (1, DateTime.UtcNow),
            (_, existing) => (existing.Count + 1, DateTime.UtcNow));
    }

    private static void ClearFailedAttempts(string clientIp)
    {
        _failedAttempts.TryRemove(clientIp, out _);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
