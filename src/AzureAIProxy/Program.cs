using Azure.Data.Tables;
using Azure.Identity;
using AzureAIProxy.Middleware;
using AzureAIProxy.Routes;
using AzureAIProxy.Services;
using AzureAIProxy.Shared.Services;
using AzureAIProxy.Management;
using AzureAIProxy.Management.Services;
using AzureAIProxy.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
var useMockProxy = builder.Configuration.GetValue<bool>("UseMockProxy", false);

// --- Table Storage ---
var storageConnectionString = builder.Configuration.GetConnectionString("StorageAccount")
    ?? builder.Configuration["StorageAccountConnectionString"];

if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddSingleton(new TableServiceClient(storageConnectionString));
}
else
{
    // Use Managed Identity in production
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
// Cookie auth for admin UI + proxy API key auth schemes
builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.Cookie.Name = "AzureAIProxy.Auth";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // Don't redirect API calls — return 401/403 directly
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
    })
    .AddScheme<ProxyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ProxyAuthenticationOptions.ApiKeyScheme,
        _ => { }
    )
    .AddScheme<ProxyAuthenticationOptions, JwtAuthenticationHandler>(
        ProxyAuthenticationOptions.JwtScheme,
        _ => { }
    )
    .AddScheme<ProxyAuthenticationOptions, BearerTokenAuthenticationHandler>(
        ProxyAuthenticationOptions.BearerTokenScheme,
        _ => { }
    );

// Authorization - no global fallback policy (would block Blazor framework files).
// Admin UI auth is enforced by <AuthorizeRouteView> in Routes.razor.
// Proxy API routes use their own [ApiKeyAuthorize]/[JwtAuthorize]/[BearerTokenAuthorize] attributes.
builder.Services.AddAuthorization();

// --- Blazor Server (Admin UI) ---
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRazorPages();
builder.Services.AddMudServices();

// --- Admin Management Services ---
builder.Services.AddManagementServices();

// --- Proxy Services ---
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IProxyService, ProxyService>();
builder.Services.AddProxyServices(useMockProxy);

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

app.UseAntiforgery();

// Proxy-specific middleware - only apply to API routes to avoid interfering with Blazor
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    appBuilder =>
    {
        appBuilder.UseMiddleware<RateLimiterHandler>();
        appBuilder.UseMiddleware<LoadProperties>();
        appBuilder.UseMiddleware<MaxTokensHandler>();
    }
);

// Map Razor Pages (login/logout)
app.MapRazorPages();

// Map Proxy API routes
app.MapProxyRoutes();

// Backup download endpoint (encrypted with user-supplied passphrase)
app.MapGet("/api/admin/backup", async (IBackupService backupService, HttpContext context) =>
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
    var fileName = $"aiproxy-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.enc";
    return Results.File(encryptedBytes, "application/octet-stream", fileName);
}).RequireAuthorization();

// Map Blazor admin UI
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(AzureAIProxy.Management.Components.Routes).Assembly);

app.Run();
