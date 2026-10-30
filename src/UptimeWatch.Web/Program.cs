using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Web.Auth;
using UptimeWatch.Web.Background;
using UptimeWatch.Web.Components;
using UptimeWatch.Web.Components.Account;
using UptimeWatch.Web.Hubs;
using UptimeWatch.Web.Realtime;

var builder = WebApplication.CreateBuilder(args);

// Fail fast: a missing connection string must not surface later as an obscure runtime error.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:Default' is not configured. Set it via the environment variable " +
        "ConnectionStrings__Default, user secrets or appsettings.<Environment>.json.");
}

// Container deployments persist the Data Protection keys (auth cookies, antiforgery) to a mounted directory so
// sessions survive a restart. Without DataProtection:KeysPath (local dotnet run, tests) the framework default applies.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("UptimeWatch")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Identity runs on Infrastructure's AppDbContext / ApplicationUser.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

// SignalR clients cannot follow a redirect to the login page: answer anonymous hub requests with 401 instead.
builder.Services.ConfigureApplicationCookie(options =>
{
    var redirectToLogin = options.Events.OnRedirectToLogin;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments(MonitorHub.Route))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return redirectToLogin(context);
    };
});

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        // There is no e-mail sender in this project, so accounts cannot be confirmed by e-mail.
        // Password rules stay at the Identity defaults.
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Infrastructure (EF Core/PostgreSQL, repositories, HTTP health checker, TimeProvider, notification channel) and application services.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<MonitorService>();
builder.Services.AddScoped<CheckProcessor>();
builder.Services.AddSingleton<CheckRunner>();
builder.Services.AddSingleton<TelegramTestMessageService>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<MonitorUpdatesBroadcaster>();
builder.Services.AddSingleton<IMonitorNotifications, SignalRMonitorNotifications>();

// Background workers; tests switch them off with Scheduler:Enabled=false.
var schedulerEnabled = builder.Configuration.GetValue("Scheduler:Enabled", true);
if (schedulerEnabled)
{
    builder.Services.AddHostedService<MonitorScheduler>();
    builder.Services.AddHostedService<RetentionCleaner>();
}

var app = builder.Build();

if (!schedulerEnabled)
{
    app.Logger.LogWarning("Scheduler:Enabled is false: monitors are NOT checked and old check results are NOT cleaned up.");
}

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// In the container the app listens on plain HTTP and TLS (if any) is terminated by a reverse proxy, so HSTS and the
// HTTPS redirect only make sense when the app itself has an HTTPS endpoint (URLS, Kestrel:Endpoints:*:Url) or an
// https port is configured (HTTPS_PORT/HTTPS_PORTS, i.e. ASPNETCORE_HTTPS_PORT(S)). Behind a TLS-terminating proxy
// the proxy has to send the HSTS header itself.
var servesHttps = (app.Configuration["URLS"] ?? string.Empty).Contains("https://", StringComparison.OrdinalIgnoreCase)
    || !string.IsNullOrEmpty(app.Configuration["HTTPS_PORT"])
    || !string.IsNullOrEmpty(app.Configuration["HTTPS_PORTS"])
    || app.Configuration.GetSection("Kestrel:Endpoints").GetChildren()
        .Any(endpoint => endpoint["Url"]?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (servesHttps)
    {
        // The default HSTS value is 30 days; see https://aka.ms/aspnetcore-hsts before raising it for production.
        app.UseHsts();
    }
}

if (servesHttps || app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Endpoints required by the Identity /Account Razor components (logout, external login, personal data).
app.MapAdditionalIdentityEndpoints();

app.MapHub<MonitorHub>(MonitorHub.Route);

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
