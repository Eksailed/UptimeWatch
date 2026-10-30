using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure.Checks;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Notifications;
using UptimeWatch.Infrastructure.Persistence;

namespace UptimeWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<HealthCheckerOptions>();
        services.TryAddSingleton<IDnsResolver, SystemDnsResolver>();
        services.TryAddSingleton<IHealthChecker, HttpHealthChecker>();

        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");
            options.UseNpgsql(connectionString);
        });
        services.AddScoped<IMonitorRepository, MonitorRepository>();
        services.AddScoped<ICheckResultRepository, CheckResultRepository>();
        services.AddScoped<IUserProfileStore, UserProfileStore>();

        AddNotifications(services, configuration);

        return services;
    }

    /// <summary>
    /// The single place that decides the notification channel: a non-empty Telegram:BotToken (user secrets or the
    /// environment variable Telegram__BotToken) selects Telegram, otherwise alerts are only logged.
    /// A later registration of INotifier (tests) overrides this one.
    /// </summary>
    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        var options = new TelegramOptions
        {
            BotToken = configuration[$"{TelegramOptions.SectionName}:BotToken"]?.Trim() ?? string.Empty
        };
        services.AddSingleton(options);

        if (!options.IsConfigured)
        {
            services.AddSingleton<ITelegramSender, NullTelegramSender>();
            services.AddSingleton<INotifier, NullNotifier>();
            return;
        }

        if (!TelegramOptions.IsValidToken(options.BotToken))
        {
            // Fail fast, without echoing the value.
            throw new InvalidOperationException("Configuration value 'Telegram:BotToken' does not look like a Telegram bot token.");
        }

        services.AddSingleton<ITelegramSender>(_ => TelegramSender.Create(options));
        services.AddScoped<INotifier, TelegramNotifier>();
    }
}
