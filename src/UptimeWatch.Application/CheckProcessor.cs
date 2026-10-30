using Microsoft.Extensions.Logging;
using UptimeWatch.Domain;

namespace UptimeWatch.Application;

public sealed class CheckProcessor(
    IHealthChecker checker,
    IMonitorRepository monitors,
    ICheckResultRepository results,
    INotifier notifier,
    IMonitorNotifications notifications,
    TimeProvider time,
    ILogger<CheckProcessor> log)
{
    public async Task ProcessAsync(SiteMonitor monitor, CancellationToken ct)
    {
        CheckResult result;
        try
        {
            result = await checker.CheckAsync(monitor, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Monitor state is untouched; its lease expires and it is picked up again.
            log.LogError(ex, "Health check failed unexpectedly for monitor {MonitorId}", monitor.Id);
            return;
        }

        var now = time.GetUtcNow().UtcDateTime;

        // ApplyResult nulls OpenIncident on close, so remember the incident that was open before.
        var openBefore = monitor.OpenIncident;
        var transition = monitor.ApplyResult(result, now);
        var closedIncident = transition == IncidentTransition.Closed ? openBefore : null;

        if (!await monitors.SaveCheckAsync(monitor, result, closedIncident, ct))
        {
            log.LogInformation("Monitor {MonitorId} was deleted or superseded by a newer state (stale copy) during the check; result discarded", monitor.Id);
            return;
        }

        try
        {
            var status = await MonitorStatusBuilder.BuildAsync(monitor, results, now, result, ct);
            await notifications.PublishAsync(monitor.OwnerId, status, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Failed to publish status of monitor {MonitorId}", monitor.Id);
        }

        try
        {
            switch (transition)
            {
                case IncidentTransition.Opened:
                    await notifier.NotifyDownAsync(monitor, monitor.OpenIncident!, ct);
                    break;
                case IncidentTransition.Closed:
                    await notifier.NotifyRecoveredAsync(monitor, closedIncident!, ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Failed to send notification for monitor {MonitorId}", monitor.Id);
        }
    }
}
