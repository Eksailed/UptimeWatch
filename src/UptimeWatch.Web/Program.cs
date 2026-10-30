using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Notifications;
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
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Infrastructure (EF Core/PostgreSQL, repositories, HTTP health checker, TimeProvider) and application services.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<MonitorService>();
builder.Services.AddScoped<CheckProcessor>();
builder.Services.AddSingleton<CheckRunner>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IMonitorNotifications, SignalRMonitorNotifications>();

// Fallback notification channel. The LAST registration of a service type wins, so a later task overrides it
// by registering its implementation after this line (Telegram when Telegram:BotToken is set).
builder.Services.AddSingleton<INotifier, NullNotifier>();

// Background workers; tests switch them off with Scheduler:Enabled=false.
if (builder.Configuration.GetValue("Scheduler:Enabled", true))
{
    builder.Services.AddHostedService<MonitorScheduler>();
    builder.Services.AddHostedService<RetentionCleaner>();
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days; see https://aka.ms/aspnetcore-hsts before raising it for production.
    app.UseHsts();
}

app.UseHttpsRedirection();
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
