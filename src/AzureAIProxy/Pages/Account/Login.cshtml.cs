using System.Collections.Concurrent;
using System.Security.Claims;
using Azure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AzureAIProxy.Shared.Database;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Shared.TableStorage;

namespace AzureAIProxy.Pages.Account;

[AllowAnonymous]
public class LoginModel(IConfiguration configuration, ITableStorageService tableStorage) : PageModel
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly ConcurrentDictionary<string, (int Count, DateTime LastAttempt)> _failedAttempts = new();

    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (IsLockedOut(clientIp))
        {
            ErrorMessage = "Too many failed login attempts. Please try again later.";
            return Page();
        }

        var configUsername = configuration["Admin:Username"];
        var configPassword = configuration["Admin:Password"];

        if (string.IsNullOrEmpty(configUsername) || string.IsNullOrEmpty(configPassword))
        {
            ErrorMessage = "Admin credentials are not configured. Set Admin__Username and Admin__Password environment variables.";
            return Page();
        }

        if (username != configUsername || password != configPassword)
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
            await ownerTable.AddEntityAsync(new OwnerEntity
            {
                PartitionKey = "owner",
                RowKey = username,
                Name = username,
                Email = $"{username}@admin"
            });
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

        return LocalRedirect(ReturnUrl ?? "/");
    }

    private static bool IsLockedOut(string clientIp)
    {
        if (!_failedAttempts.TryGetValue(clientIp, out var entry))
            return false;

        if (entry.Count >= MaxFailedAttempts && DateTime.UtcNow - entry.LastAttempt < LockoutDuration)
            return true;

        // Lockout expired — clear the entry
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
}
