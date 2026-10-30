using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UptimeWatch.Domain;

namespace UptimeWatch.Application.Tests;

public class CheckRunnerTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private sealed class TrackingChecker : IHealthChecker
    {
        private int _current;
        private int _peak;
        private int _total;

        public int Peak => _peak;
        public int Total => _total;

        public async Task<CheckResult> CheckAsync(SiteMonitor monitor, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            while (now > (seen = Volatile.Read(ref _peak)))
            {
                Interlocked.CompareExchange(ref _peak, now, seen);
            }

            try
            {
                await Task.Delay(50, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
                Interlocked.Increment(ref _total);
            }

            return CheckResult.Create(monitor.Id, Time.GetUtcNow().UtcDateTime, true, 200, 50, null);
        }
    }

    private static List<SiteMonitor> Monitors(int count) =>
        Enumerable.Range(0, count)
            .Select(i => SiteMonitor.Create("u1", $"https://example.com/{i}", 60, 200, Time.GetUtcNow().UtcDateTime))
            .ToList();

    private static ServiceProvider Build(
        IReadOnlyList<SiteMonitor> due, IHealthChecker checker, IMonitorRepository? repoOverride = null)
    {
        var repo = repoOverride ?? Substitute.For<IMonitorRepository>();
        repo.ClaimDueAsync(default, default, default, default).ReturnsForAnyArgs(due);
        if (repoOverride is null)
        {
            repo.SaveCheckAsync(default!, default!, default, default).ReturnsForAnyArgs(true);
        }

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(repo);
        services.AddSingleton(checker);
        services.AddSingleton(Substitute.For<ICheckResultRepository>());
        services.AddSingleton(Substitute.For<INotifier>());
        services.AddSingleton(Substitute.For<IMonitorNotifications>());
        services.AddScoped<CheckProcessor>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunDue_never_runs_more_than_20_checks_concurrently()
    {
        var checker = new TrackingChecker();
        await using var provider = Build(Monitors(50), checker);
        var sut = new CheckRunner(provider.GetRequiredService<IServiceScopeFactory>(), Time);

        await sut.RunDueAsync(CancellationToken.None);

        Assert.Equal(50, checker.Total);
        Assert.InRange(checker.Peak, 2, 20);
    }

    [Fact]
    public async Task RunDue_continues_when_one_check_throws()
    {
        var due = Monitors(10);
        var failing = due[3];
        var repo = Substitute.For<IMonitorRepository>();
        repo.SaveCheckAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
            ReferenceEquals(call.ArgAt<SiteMonitor>(0), failing)
                ? Task.FromException<bool>(new InvalidOperationException("db down"))
                : Task.FromResult(true));
        var checker = new TrackingChecker();
        await using var provider = Build(due, checker, repo);
        var sut = new CheckRunner(provider.GetRequiredService<IServiceScopeFactory>(), Time);

        await sut.RunDueAsync(CancellationToken.None);

        Assert.Equal(10, checker.Total);
        await repo.ReceivedWithAnyArgs(10).SaveCheckAsync(default!, default!, default, default);
    }
}
