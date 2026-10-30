using Microsoft.AspNetCore.SignalR;
using UptimeWatch.Application;
using UptimeWatch.Web.Hubs;

namespace UptimeWatch.Web.Realtime;

/// <summary>
/// Sends monitor status updates to the dashboards of the monitor's owner only: through the SignalR hub group
/// (browsers/other clients) and through the in-process broadcaster (Blazor circuits of this app).
/// The two paths run concurrently and one failing path never prevents the other.
/// </summary>
public sealed class SignalRMonitorNotifications(IHubContext<MonitorHub> hub, MonitorUpdatesBroadcaster broadcaster) : IMonitorNotifications
{
    public async Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct)
    {
        // Task.WhenAll rethrows only the first failure of an awaited task, so every path reports into one list.
        var errors = new List<Exception>();

        await Task.WhenAll(
            Guarded(() => hub.Clients.Group(MonitorHub.OwnerGroup(ownerId)).SendAsync(MonitorHub.MonitorUpdatedEvent, status, ct), errors),
            Guarded(() => broadcaster.PublishAsync(ownerId, status), errors));

        if (errors.Count > 0)
        {
            throw new AggregateException(errors);
        }
    }

    // Also catches exceptions thrown synchronously while the call is being started.
    private static async Task Guarded(Func<Task> publish, List<Exception> errors)
    {
        try
        {
            await publish();
        }
        catch (Exception ex)
        {
            lock (errors)
            {
                errors.Add(ex);
            }
        }
    }
}
