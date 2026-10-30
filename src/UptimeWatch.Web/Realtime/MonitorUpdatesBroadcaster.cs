using UptimeWatch.Application;

namespace UptimeWatch.Web.Realtime;

/// <summary>
/// In-process fan-out of monitor status updates to the Blazor circuits of the same app, so dashboards do not need
/// a SignalR connection to themselves. Subscriptions are per owner: a handler only ever sees its own owner's updates.
/// </summary>
public sealed class MonitorUpdatesBroadcaster(ILogger<MonitorUpdatesBroadcaster> log)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Subscription>> _subscriptions = [];

    /// <summary>Registers a handler for one owner. Dispose the result to unsubscribe (idempotent).</summary>
    public IDisposable Subscribe(string ownerId, Func<MonitorStatusDto, Task> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new Subscription(this, ownerId, handler);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(ownerId, out var list))
            {
                _subscriptions[ownerId] = list = [];
            }

            list.Add(subscription);
        }

        return subscription;
    }

    /// <summary>Calls every handler of the owner. A throwing handler is logged and does not affect the others.</summary>
    public async Task PublishAsync(string ownerId, MonitorStatusDto status)
    {
        Subscription[] snapshot;
        lock (_gate)
        {
            snapshot = _subscriptions.TryGetValue(ownerId, out var list) ? [.. list] : [];
        }

        await Task.WhenAll(snapshot.Select(s => InvokeAsync(s, status)));
    }

    private async Task InvokeAsync(Subscription subscription, MonitorStatusDto status)
    {
        try
        {
            await subscription.Handler(status);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "A dashboard subscriber failed to handle the update of monitor {MonitorId}", status.Id);
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(subscription.OwnerId, out var list) && list.Remove(subscription) && list.Count == 0)
            {
                _subscriptions.Remove(subscription.OwnerId);
            }
        }
    }

    private sealed class Subscription(MonitorUpdatesBroadcaster owner, string ownerId, Func<MonitorStatusDto, Task> handler) : IDisposable
    {
        public string OwnerId { get; } = ownerId;

        public Func<MonitorStatusDto, Task> Handler { get; } = handler;

        public void Dispose() => owner.Unsubscribe(this);
    }
}
