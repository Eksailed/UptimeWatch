using UptimeWatch.Application;

namespace UptimeWatch.Web.Background;

/// <summary>Runs due checks in a loop: <see cref="CheckRunner.RunDueAsync"/>, then a short pause.</summary>
public sealed class MonitorScheduler(CheckRunner runner, TimeProvider time, ILogger<MonitorScheduler> log) : BackgroundService
{
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await runner.RunDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    log.LogError(ex, "Monitor scheduler iteration failed; retrying after {Pause}", Pause);
                }

                await Task.Delay(Pause, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
    }
}
