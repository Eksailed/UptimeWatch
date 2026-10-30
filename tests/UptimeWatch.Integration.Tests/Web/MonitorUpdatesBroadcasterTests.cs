using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UptimeWatch.Application;
using UptimeWatch.Web.Hubs;
using UptimeWatch.Web.Realtime;

namespace UptimeWatch.Integration.Tests.Web;

public sealed class MonitorUpdatesBroadcasterTests
{
    private readonly MonitorUpdatesBroadcaster _broadcaster = new(NullLogger<MonitorUpdatesBroadcaster>.Instance);

    private static MonitorStatusDto Status() =>
        new(Guid.NewGuid(), "https://example.com", 60, 200, true, false, 200, 15, DateTime.UtcNow, new UptimeDto(100, 100, 100));

    [Fact]
    public async Task Delivers_only_to_subscribers_of_the_matching_owner()
    {
        var forA = new List<Guid>();
        var forB = new List<Guid>();
        using var a = _broadcaster.Subscribe("owner-a", s => { lock (forA) forA.Add(s.Id); return Task.CompletedTask; });
        using var b = _broadcaster.Subscribe("owner-b", s => { lock (forB) forB.Add(s.Id); return Task.CompletedTask; });
        var first = Status();
        var second = Status();

        // Handlers run inside PublishAsync, so after the awaits the outcome is final: no timing involved.
        await _broadcaster.PublishAsync("owner-a", first);
        await _broadcaster.PublishAsync("owner-b", second);

        Assert.Equal([first.Id], forA);
        Assert.Equal([second.Id], forB);
    }

    [Fact]
    public async Task Unsubscribe_stops_delivery_and_is_idempotent()
    {
        var calls = 0;
        var subscription = _broadcaster.Subscribe("owner-a", _ => { calls++; return Task.CompletedTask; });
        await _broadcaster.PublishAsync("owner-a", Status());

        subscription.Dispose();
        subscription.Dispose();
        await _broadcaster.PublishAsync("owner-a", Status());

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Throwing_handler_does_not_block_other_subscribers()
    {
        var delivered = 0;
        using var failing = _broadcaster.Subscribe("owner-a", _ => throw new InvalidOperationException("sync failure"));
        using var failingAsync = _broadcaster.Subscribe("owner-a", async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async failure");
        });
        using var healthy = _broadcaster.Subscribe("owner-a", _ => { delivered++; return Task.CompletedTask; });

        await _broadcaster.PublishAsync("owner-a", Status());

        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task Notifications_still_reach_broadcaster_when_the_hub_fails()
    {
        var delivered = 0;
        var proxy = Substitute.For<IClientProxy>();
        proxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("hub failure"));
        var hub = Substitute.For<IHubContext<MonitorHub>>();
        hub.Clients.Group("owner-owner-a").Returns(proxy);
        using var subscription = _broadcaster.Subscribe("owner-a", _ => { delivered++; return Task.CompletedTask; });
        var notifications = new SignalRMonitorNotifications(hub, _broadcaster);

        await Assert.ThrowsAsync<AggregateException>(() => notifications.PublishAsync("owner-a", Status(), CancellationToken.None));

        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task Notifications_still_reach_the_hub_group_when_a_subscriber_fails()
    {
        var proxy = Substitute.For<IClientProxy>();
        var hub = Substitute.For<IHubContext<MonitorHub>>();
        hub.Clients.Group("owner-owner-a").Returns(proxy);
        using var subscription = _broadcaster.Subscribe("owner-a", _ => throw new InvalidOperationException("ui failure"));
        var notifications = new SignalRMonitorNotifications(hub, _broadcaster);

        await notifications.PublishAsync("owner-a", Status(), CancellationToken.None);

        await proxy.Received(1).SendCoreAsync(MonitorHub.MonitorUpdatedEvent, Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
