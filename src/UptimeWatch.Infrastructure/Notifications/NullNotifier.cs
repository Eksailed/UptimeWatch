using Microsoft.Extensions.Logging;
using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>Fallback <see cref="INotifier"/> used when no real channel (Telegram) is configured: logs and does nothing.</summary>
public sealed class NullNotifier(ILogger<NullNotifier> log) : INotifier
{
    public Task NotifyDownAsync(SiteMonitor monitor, Incident incident, CancellationToken ct)
    {
        log.LogWarning("No notification channel is configured; monitor {MonitorId} is DOWN and nobody was notified", monitor.Id);
        return Task.CompletedTask;
    }

    public Task NotifyRecoveredAsync(SiteMonitor monitor, Incident incident, CancellationToken ct)
    {
        log.LogWarning("No notification channel is configured; monitor {MonitorId} has recovered and nobody was notified", monitor.Id);
        return Task.CompletedTask;
    }
}
