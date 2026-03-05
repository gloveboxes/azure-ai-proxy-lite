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
    public string? ErrorMessage { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        var configUsername = configuration["Admin:Username"];
        var configPassword = configuration["Admin:Password"];

        if (string.IsNullOrEmpty(configUsername) || string.IsNullOrEmpty(configPassword))
        {
            ErrorMessage = "Admin credentials are not configured. Set Admin__Username and Admin__Password environment variables.";
            return Page();
        }

        if (username != configUsername || password != configPassword)
        {
            ErrorMessage = "Invalid username or password.";
            return Page();
        }

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
}
