using Microsoft.AspNetCore.SignalR;
using UptimeWatch.Application;
using UptimeWatch.Web.Hubs;

namespace UptimeWatch.Web.Realtime;

/// <summary>Sends monitor status updates to the dashboards of the monitor's owner only.</summary>
public sealed class SignalRMonitorNotifications(IHubContext<MonitorHub> hub) : IMonitorNotifications
{
    public Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct) =>
        hub.Clients.Group(MonitorHub.OwnerGroup(ownerId)).SendAsync(MonitorHub.MonitorUpdatedEvent, status, ct);
}
