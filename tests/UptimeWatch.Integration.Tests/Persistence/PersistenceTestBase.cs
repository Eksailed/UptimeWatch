using UptimeWatch.Domain;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

[Collection(PostgresCollection.Name)]
public abstract class PersistenceTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected static readonly DateTime T = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    protected PostgresFixture Postgres { get; } = postgres;

    public Task InitializeAsync() => Postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Runs the action with a repository on a brand-new DbContext (a "different DI scope").</summary>
    protected async Task<TResult> WithMonitors<TResult>(Func<MonitorRepository, Task<TResult>> action)
    {
        await using var context = Postgres.CreateContext();
        return await action(new MonitorRepository(context));
    }

    protected async Task WithMonitors(Func<MonitorRepository, Task> action)
    {
        await using var context = Postgres.CreateContext();
        await action(new MonitorRepository(context));
    }

    protected async Task<TResult> WithResults<TResult>(Func<CheckResultRepository, Task<TResult>> action)
    {
        await using var context = Postgres.CreateContext();
        return await action(new CheckResultRepository(context));
    }

    protected static SiteMonitor NewMonitor(string owner = "alice", string url = "https://example.com", DateTime? now = null) =>
        SiteMonitor.Create(owner, url, 60, 200, now ?? T);

    protected static CheckResult Failure(Guid monitorId, DateTime at, string error = "boom") =>
        CheckResult.Create(monitorId, at, false, null, 100, error);

    protected static CheckResult Success(Guid monitorId, DateTime at) =>
        CheckResult.Create(monitorId, at, true, 200, 50, null);

    /// <summary>Adds the monitor, then applies and saves failing checks (each in a new context) until it has an open incident.</summary>
    protected async Task<SiteMonitor> AddMonitorWithOpenIncident(string error = "boom", string owner = "alice")
    {
        var monitor = NewMonitor(owner);
        await WithMonitors(r => r.AddAsync(monitor, CancellationToken.None));

        for (var i = 0; i < MonitorLimits.FailureThreshold; i++)
        {
            var at = T.AddMinutes(i + 1);
            var result = Failure(monitor.Id, at, error);
            monitor.ApplyResult(result, at);
            Assert.True(await WithMonitors(r => r.SaveCheckAsync(monitor, result, null, CancellationToken.None)));
        }

        Assert.NotNull(monitor.OpenIncident);
        return monitor;
    }
}
