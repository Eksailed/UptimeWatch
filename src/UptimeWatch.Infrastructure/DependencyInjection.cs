using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure.Checks;
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

        return services;
    }
}
