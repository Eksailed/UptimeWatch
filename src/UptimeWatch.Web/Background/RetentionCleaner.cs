using UptimeWatch.Application;

namespace UptimeWatch.Web.Background;

/// <summary>Deletes check results older than <see cref="Retention"/>: shortly after startup, then every 24 hours.</summary>
public sealed class RetentionCleaner(IServiceScopeFactory scopes, TimeProvider time, ILogger<RetentionCleaner> log) : BackgroundService
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, time, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanAsync(stoppingToken);
                await Task.Delay(Interval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
    }

    private async Task CleanAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var results = scope.ServiceProvider.GetRequiredService<ICheckResultRepository>();
            var cutoff = time.GetUtcNow().UtcDateTime - Retention;
            var deleted = await results.DeleteOlderThanAsync(cutoff, ct);
            log.LogInformation("Retention cleanup removed {Count} check results older than {Cutoff:u}", deleted, cutoff);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Retention cleanup failed; will retry in {Interval}", Interval);
        }
    }
}
