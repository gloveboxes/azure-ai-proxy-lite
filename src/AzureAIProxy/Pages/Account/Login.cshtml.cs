using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Pages.Account;

[AllowAnonymous]
public class LoginModel(IConfiguration configuration, IDbContextFactory<AzureAIProxyDbContext> dbFactory) : PageModel
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

        // Ensure the owner record exists in the database
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Owners.AnyAsync(o => o.OwnerId == username))
        {
            db.Owners.Add(new Owner
            {
                OwnerId = username,
                Name = username,
                Email = $"{username}@admin"
            });
            await db.SaveChangesAsync();
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
