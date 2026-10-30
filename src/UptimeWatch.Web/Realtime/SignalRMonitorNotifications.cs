using Microsoft.AspNetCore.SignalR;
using UptimeWatch.Application;
using UptimeWatch.Web.Hubs;

namespace UptimeWatch.Web.Realtime;

/// <summary>
/// Sends monitor status updates to the dashboards of the monitor's owner only: through the SignalR hub group
/// (browsers/other clients) and through the in-process broadcaster (Blazor circuits of this app).
/// One failing path never prevents the other.
/// </summary>
public sealed class SignalRMonitorNotifications(IHubContext<MonitorHub> hub, MonitorUpdatesBroadcaster broadcaster) : IMonitorNotifications
{
    public async Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct)
    {
        List<Exception>? errors = null;

        try
        {
            await hub.Clients.Group(MonitorHub.OwnerGroup(ownerId)).SendAsync(MonitorHub.MonitorUpdatedEvent, status, ct);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        try
        {
            await broadcaster.PublishAsync(ownerId, status);
        }
        catch (Exception ex)
        {
            (errors ??= []).Add(ex);
        }

        if (errors is not null)
        {
            throw new AggregateException(errors);
        }
    }
}
