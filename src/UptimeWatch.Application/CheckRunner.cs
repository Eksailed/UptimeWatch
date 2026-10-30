using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UptimeWatch.Domain;

namespace UptimeWatch.Application;

public sealed class CheckRunner(IServiceScopeFactory scopes, TimeProvider time)
{
    private const int MaxConcurrentChecks = 20;
    private const int ClaimBatchSize = 100;
    private static readonly TimeSpan ClaimLease = TimeSpan.FromSeconds(60);

    /// <summary>Claims due monitors and checks them in parallel, each in its own DI scope.</summary>
    public async Task RunDueAsync(CancellationToken ct)
    {
        IReadOnlyList<SiteMonitor> due;
        ILogger log;
        using (var scope = scopes.CreateScope())
        {
            log = scope.ServiceProvider.GetService<ILogger<CheckRunner>>() ?? NullLogger<CheckRunner>.Instance;
            var repository = scope.ServiceProvider.GetRequiredService<IMonitorRepository>();
            due = await repository.ClaimDueAsync(time.GetUtcNow().UtcDateTime, ClaimBatchSize, ClaimLease, ct);
        }

        if (due.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(MaxConcurrentChecks);
        await Task.WhenAll(due.Select(monitor => RunOneAsync(monitor, gate, log, ct)));
    }

    private async Task RunOneAsync(SiteMonitor monitor, SemaphoreSlim gate, ILogger log, CancellationToken ct)
    {
        try
        {
            await gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var scope = scopes.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<CheckProcessor>();
            await processor.ProcessAsync(monitor, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(ex, "Processing monitor {MonitorId} failed", monitor.Id);
        }
        finally
        {
            gate.Release();
        }
    }
}
