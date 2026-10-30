using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UptimeWatch.Domain;

namespace UptimeWatch.Application.Tests;

public class CheckProcessorTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly IHealthChecker _checker = Substitute.For<IHealthChecker>();
    private readonly IMonitorRepository _monitors = Substitute.For<IMonitorRepository>();
    private readonly ICheckResultRepository _results = Substitute.For<ICheckResultRepository>();
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private readonly IMonitorNotifications _notifications = Substitute.For<IMonitorNotifications>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly CheckProcessor _sut;

    public CheckProcessorTests()
    {
        _monitors.SaveCheckAsync(default!, default!, default, default).ReturnsForAnyArgs(true);
        _sut = new CheckProcessor(
            _checker, _monitors, _results, _notifier, _notifications, _time, NullLogger<CheckProcessor>.Instance);
    }

    private SiteMonitor NewMonitor() =>
        SiteMonitor.Create("u1", "https://example.com", 60, 200, _time.GetUtcNow().UtcDateTime);

    private CheckResult Ok(SiteMonitor m) => CheckResult.Create(m.Id, _time.GetUtcNow().UtcDateTime, true, 200, 42, null);

    private CheckResult Fail(SiteMonitor m) =>
        CheckResult.Create(m.Id, _time.GetUtcNow().UtcDateTime, false, 500, 42, "HTTP 500");

    private SiteMonitor MonitorWithFailures(int failures)
    {
        var m = NewMonitor();
        for (var i = 0; i < failures; i++)
        {
            m.ApplyResult(Fail(m), _time.GetUtcNow().UtcDateTime);
        }

        return m;
    }

    [Fact]
    public async Task Process_saves_result_and_publishes_status_to_owner()
    {
        var m = NewMonitor();
        var result = Ok(m);
        _checker.CheckAsync(m, Ct).Returns(result);

        await _sut.ProcessAsync(m, Ct);

        await _monitors.Received(1).SaveCheckAsync(m, result, null, Ct);
        await _notifications.Received(1).PublishAsync(
            "u1",
            Arg.Is<MonitorStatusDto>(s => s.Id == m.Id && !s.IsDown && s.LastStatusCode == 200 && s.LastResponseTimeMs == 42),
            Ct);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRecoveredAsync(default!, default!, default);
    }

    [Fact]
    public async Task Process_third_failure_opens_incident_and_calls_NotifyDown_once()
    {
        var m = MonitorWithFailures(2);
        var result = Fail(m);
        _checker.CheckAsync(m, Ct).Returns(result);

        await _sut.ProcessAsync(m, Ct);

        Assert.NotNull(m.OpenIncident);
        await _monitors.Received(1).SaveCheckAsync(m, result, null, Ct);
        await _notifier.Received(1).NotifyDownAsync(m, m.OpenIncident!, Ct);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRecoveredAsync(default!, default!, default);
        await _notifications.Received(1).PublishAsync("u1", Arg.Is<MonitorStatusDto>(s => s.IsDown), Ct);
    }

    [Fact]
    public async Task Process_fourth_failure_does_not_notify_again()
    {
        var m = MonitorWithFailures(3);
        var openIncident = m.OpenIncident;
        Assert.NotNull(openIncident);
        var result = Fail(m);
        _checker.CheckAsync(m, Ct).Returns(result);

        await _sut.ProcessAsync(m, Ct);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRecoveredAsync(default!, default!, default);
        await _monitors.Received(1).SaveCheckAsync(m, result, null, Ct);
        Assert.Same(openIncident, m.OpenIncident);
    }

    [Fact]
    public async Task Process_success_after_incident_calls_NotifyRecovered_with_closed_incident()
    {
        var m = MonitorWithFailures(3);
        var openIncident = m.OpenIncident!;
        var result = Ok(m);
        _checker.CheckAsync(m, Ct).Returns(result);
        _time.Advance(TimeSpan.FromMinutes(5));

        await _sut.ProcessAsync(m, Ct);

        Assert.Null(m.OpenIncident);
        Assert.NotNull(openIncident.EndedAt);
        await _notifier.Received(1).NotifyRecoveredAsync(m, openIncident, Ct);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);
        await _notifications.Received(1).PublishAsync("u1", Arg.Is<MonitorStatusDto>(s => !s.IsDown), Ct);
    }

    [Fact]
    public async Task Process_passes_previously_open_incident_to_SaveCheck_when_closing()
    {
        var m = MonitorWithFailures(3);
        var openIncident = m.OpenIncident!;
        var result = Ok(m);
        _checker.CheckAsync(m, Ct).Returns(result);

        await _sut.ProcessAsync(m, Ct);

        await _monitors.Received(1).SaveCheckAsync(m, result, openIncident, Ct);
    }

    [Fact]
    public async Task Process_passes_null_closed_incident_when_opening()
    {
        var m = MonitorWithFailures(2);
        var result = Fail(m);
        _checker.CheckAsync(m, Ct).Returns(result);

        await _sut.ProcessAsync(m, Ct);

        await _monitors.Received(1).SaveCheckAsync(m, result, null, Ct);
    }

    [Fact]
    public async Task Process_swallows_notifier_exception_and_state_is_still_saved()
    {
        var m = MonitorWithFailures(2);
        var result = Fail(m);
        _checker.CheckAsync(m, Ct).Returns(result);
        _notifier.NotifyDownAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("telegram down"));

        await _sut.ProcessAsync(m, Ct);

        await _monitors.Received(1).SaveCheckAsync(m, result, null, Ct);
        await _notifications.Received(1).PublishAsync("u1", Arg.Any<MonitorStatusDto>(), Ct);
    }

    [Fact]
    public async Task Process_swallows_publish_exception_and_still_notifies()
    {
        var m = MonitorWithFailures(2);
        _checker.CheckAsync(m, Ct).Returns(Fail(m));
        _notifications.PublishAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("hub down"));

        await _sut.ProcessAsync(m, Ct);

        await _notifier.Received(1).NotifyDownAsync(m, m.OpenIncident!, Ct);
    }

    [Fact]
    public async Task Process_does_not_notify_or_publish_when_SaveCheck_returns_false()
    {
        var m = MonitorWithFailures(2);
        _checker.CheckAsync(m, Ct).Returns(Fail(m));
        _monitors.SaveCheckAsync(default!, default!, default, default).ReturnsForAnyArgs(false);

        await _sut.ProcessAsync(m, Ct);

        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRecoveredAsync(default!, default!, default);
        await _notifications.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default);
    }

    [Fact]
    public async Task Process_does_not_throw_when_health_checker_throws()
    {
        var m = NewMonitor();
        var nextCheckBefore = m.NextCheckAt;
        _checker.CheckAsync(m, Ct).ThrowsAsync(new InvalidOperationException("boom"));

        await _sut.ProcessAsync(m, Ct);

        Assert.Equal(0, m.ConsecutiveFailures);
        Assert.Equal(nextCheckBefore, m.NextCheckAt);
        await _monitors.DidNotReceiveWithAnyArgs().SaveCheckAsync(default!, default!, default, default);
        await _notifications.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default);
    }
}
