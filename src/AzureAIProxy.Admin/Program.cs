using Azure.Data.Tables;
using Azure.Identity;
using AzureAIProxy.Admin.Components;
using AzureAIProxy.Management;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Shared.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using MudBlazor.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --- Table Storage ---
var storageConnectionString = builder.Configuration.GetConnectionString("StorageAccount")
    ?? builder.Configuration["StorageAccountConnectionString"];

if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddSingleton(new TableServiceClient(storageConnectionString));
}
else
{
    var storageAccountName = builder.Configuration["StorageAccountName"]
        ?? throw new InvalidOperationException("StorageAccountName or StorageAccount connection string must be configured");
    var serviceUri = new Uri($"https://{storageAccountName}.table.core.windows.net");
    builder.Services.AddSingleton(new TableServiceClient(serviceUri, new DefaultAzureCredential()));
}

builder.Services.AddSingleton<ITableStorageService, TableStorageService>();

// --- Encryption ---
var encryptionKey = builder.Configuration["EncryptionKey"]
    ?? builder.Configuration["PostgresEncryptionKey"]
    ?? throw new InvalidOperationException("EncryptionKey must be configured");
builder.Services.AddSingleton<IEncryptionService>(new EncryptionService(encryptionKey));

// --- Authentication ---
// Detect mode: Entra ID (OpenID Connect) if AzureAd:ClientId is configured, otherwise local password.
var useEntraAuth = !string.IsNullOrEmpty(builder.Configuration["AzureAd:ClientId"]);

// Fail fast if neither auth method is properly configured
if (!useEntraAuth
    && string.IsNullOrEmpty(builder.Configuration["Admin:Username"])
    && string.IsNullOrEmpty(builder.Configuration["Admin:Password"])
    && !builder.Configuration.GetSection("Admin:Users").GetChildren().Any())
{
    throw new InvalidOperationException(
        "No authentication is configured. Set AzureAd:ClientId for Entra ID auth, " +
        "or set Admin:Username and Admin:Password for local password auth, " +
        "or configure Admin:Users array for multi-user local auth.");
}

if (useEntraAuth)
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    // Suppress OIDC redirect for API calls — return 401/403 directly
    builder.Services.Configure<CookieAuthenticationOptions>(
        CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
}
else
{
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.Cookie.Name = "AzureAIProxy.Auth";
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });
}

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// --- Blazor Server (Admin UI) ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();
builder.Services.AddMudServices();

// --- Admin Management Services ---
builder.Services.AddMemoryCache();
builder.Services.AddManagementServices();

if (!string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"] ?? builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Auto-create Owner record on first authenticated request (supports both Entra and local login)
app.UseMiddleware<AzureAIProxy.Admin.Middleware.EnsureOwnerMiddleware>();

app.UseAntiforgery();

// Map Razor Pages (login/logout)
app.MapRazorPages();

// Backup download endpoint (encrypted with user-supplied passphrase)
app.MapGet("/api/admin/backup", async (IBackupService backupService, IAuthService authService, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("X-Backup-Passphrase", out var passphraseValues)
        || string.IsNullOrWhiteSpace(passphraseValues.ToString()))
    {
        return Results.BadRequest(new { error = "X-Backup-Passphrase header is required." });
    }

    var passphrase = passphraseValues.ToString();
    if (passphrase.Length < BackupService.MinPassphraseLength)
    {
        return Results.BadRequest(new { error = $"Passphrase must be at least {BackupService.MinPassphraseLength} characters." });
    }

    var encryptedBytes = await backupService.CreateEncryptedBackupAsync(passphrase);
    var (email, _) = await authService.GetCurrentUserEmailNameAsync();
    var sanitizedEmail = email.Replace("@", "_at_").Replace(".", "_");
    var fileName = $"aiproxy-backup-{sanitizedEmail}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.enc";
    return Results.File(encryptedBytes, "application/octet-stream", fileName);
}).RequireAuthorization();

// Map Blazor admin UI
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AzureAIProxy.Management.Components.Routes).Assembly);

app.Run();
