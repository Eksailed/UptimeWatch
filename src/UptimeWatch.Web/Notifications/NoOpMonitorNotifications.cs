using UptimeWatch.Application;

namespace UptimeWatch.Web.Notifications;

/// <summary>Placeholder until the SignalR-based implementation replaces it (dashboard live updates).</summary>
public sealed class NoOpMonitorNotifications : IMonitorNotifications
{
    public Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct) => Task.CompletedTask;
}
