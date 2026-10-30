using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UptimeWatch.Integration.Tests.Support;

/// <summary>The real web application on top of the test PostgreSQL container, without Telegram and background workers.</summary>
public sealed class WebAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads these values while it is still building the host.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Telegram:BotToken", "");
        builder.UseSetting("Scheduler:Enabled", "false");
    }
}
