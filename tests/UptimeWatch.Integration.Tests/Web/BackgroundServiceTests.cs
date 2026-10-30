using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UptimeWatch.Application;
using UptimeWatch.Domain;
using UptimeWatch.Web.Background;

namespace UptimeWatch.Integration.Tests.Web;

public sealed class BackgroundServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly IMonitorRepository _monitors = Substitute.For<IMonitorRepository>();
    private readonly ICheckResultRepository _results = Substitute.For<ICheckResultRepository>();
    private readonly ServiceProvider _provider;

    public BackgroundServiceTests()
    {
        _monitors.ClaimDueAsync(default, default, default, default).ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<SiteMonitor>>([]));
        _provider = new ServiceCollection()
            .AddSingleton(_monitors)
            .AddSingleton(_results)
            .BuildServiceProvider();
    }

    private MonitorScheduler Scheduler() =>
        new(new CheckRunner(_provider.GetRequiredService<IServiceScopeFactory>(), _time), _time, NullLogger<MonitorScheduler>.Instance);

    private RetentionCleaner Cleaner() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), _time, NullLogger<RetentionCleaner>.Instance);

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time");
            await Task.Delay(10);
        }
    }

    private int ClaimCalls => _monitors.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IMonitorRepository.ClaimDueAsync));

    private int DeleteCalls => _results.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICheckResultRepository.DeleteOlderThanAsync));

    [Fact]
    public async Task Scheduler_runs_due_checks_then_pauses_five_seconds()
    {
        var scheduler = Scheduler();
        await scheduler.StartAsync(CancellationToken.None);

        await Eventually(() => ClaimCalls == 1);
        _time.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(100);
        Assert.Equal(1, ClaimCalls);

        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => ClaimCalls == 2);

        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_survives_exceptions_and_keeps_looping()
    {
        _monitors.ClaimDueAsync(default, default, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var scheduler = Scheduler();
        await scheduler.StartAsync(CancellationToken.None);

        await Eventually(() => ClaimCalls == 1);
        _time.Advance(MonitorScheduler.Pause);
        await Eventually(() => ClaimCalls == 2);

        await scheduler.StopAsync(CancellationToken.None);
        Assert.Null(scheduler.ExecuteTask?.Exception);
    }

    [Fact]
    public async Task Scheduler_stops_promptly_on_cancellation()
    {
        var scheduler = Scheduler();
        await scheduler.StartAsync(CancellationToken.None);
        await Eventually(() => ClaimCalls == 1);

        await scheduler.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(scheduler.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cleaner_deletes_results_older_than_30_days_shortly_after_startup_and_then_daily()
    {
        var cleaner = Cleaner();
        await cleaner.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(0, DeleteCalls);

        _time.Advance(RetentionCleaner.StartupDelay);
        await Eventually(() => DeleteCalls == 1);
        await _results.Received(1).DeleteOlderThanAsync(
            (Now + RetentionCleaner.StartupDelay).UtcDateTime - TimeSpan.FromDays(30), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromHours(23));
        await Task.Delay(100);
        Assert.Equal(1, DeleteCalls);

        _time.Advance(TimeSpan.FromHours(1));
        await Eventually(() => DeleteCalls == 2);

        await cleaner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cleaner_survives_exceptions()
    {
        _results.DeleteOlderThanAsync(default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("db down"));
        var cleaner = Cleaner();
        await cleaner.StartAsync(CancellationToken.None);

        _time.Advance(RetentionCleaner.StartupDelay);
        await Eventually(() => DeleteCalls == 1);
        _time.Advance(RetentionCleaner.Interval);
        await Eventually(() => DeleteCalls == 2);

        await cleaner.StopAsync(CancellationToken.None);
        Assert.Null(cleaner.ExecuteTask?.Exception);
    }
}
