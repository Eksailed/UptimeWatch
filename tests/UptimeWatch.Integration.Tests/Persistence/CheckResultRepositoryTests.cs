using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Domain;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

public sealed class CheckResultRepositoryTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task<Guid> AddMonitorAsync(string owner = "alice")
    {
        var monitor = NewMonitor(owner);
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        return monitor.Id;
    }

    private async Task SaveAsync(params CheckResult[] results)
    {
        await using var db = Postgres.CreateContext();
        db.CheckResults.AddRange(results);
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Uptime_is_75_percent_for_3_successes_and_1_failure_in_window()
    {
        var id = await AddMonitorAsync();
        await SaveAsync(Success(id, T.AddMinutes(-1)), Success(id, T.AddMinutes(-2)), Success(id, T.AddMinutes(-3)), Failure(id, T.AddMinutes(-4)));

        var uptime = await WithResults(r => r.GetUptimePercentAsync(id, T.AddHours(-1), Ct));

        Assert.Equal(75.0, uptime);
    }

    [Fact]
    public async Task Uptime_ignores_results_older_than_window()
    {
        var id = await AddMonitorAsync();
        await SaveAsync(Success(id, T.AddMinutes(-1)), Failure(id, T.AddHours(-2)), Failure(id, T.AddDays(-3)));

        var uptime = await WithResults(r => r.GetUptimePercentAsync(id, T.AddHours(-1), Ct));

        Assert.Equal(100.0, uptime);
    }

    [Fact]
    public async Task Uptime_is_null_without_results()
    {
        var id = await AddMonitorAsync();
        var otherId = await AddMonitorAsync("bob");
        await SaveAsync(Success(otherId, T.AddMinutes(-1)), Failure(id, T.AddDays(-5)));

        var uptime = await WithResults(r => r.GetUptimePercentAsync(id, T.AddHours(-1), Ct));

        Assert.Null(uptime);
    }

    [Fact]
    public async Task Uptime_is_one_command_without_ef_warnings()
    {
        var id = await AddMonitorAsync();
        await SaveAsync(Success(id, T.AddMinutes(-1)), Failure(id, T.AddMinutes(-2)));
        var log = new List<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Postgres.ConnectionString)
            .LogTo(log.Add, LogLevel.Information)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            Assert.Equal(50.0, await new CheckResultRepository(db).GetUptimePercentAsync(id, T.AddHours(-1), Ct));
        }

        Assert.Single(log, line => line.Contains("Executed DbCommand"));
        Assert.DoesNotContain(log, line => line.Contains("warn:"));
    }

    [Fact]
    public async Task DeleteOlderThan_removes_only_results_before_cutoff()
    {
        var id = await AddMonitorAsync();
        var cutoff = T.AddDays(-30);
        var boundary = Success(id, cutoff);
        var fresh = Success(id, T);
        await SaveAsync(Success(id, cutoff.AddSeconds(-1)), Failure(id, cutoff.AddDays(-10)), boundary, fresh);

        var deleted = await WithResults(r => r.DeleteOlderThanAsync(cutoff, Ct));

        Assert.Equal(2, deleted);
        await using var db = Postgres.CreateContext();
        var left = await db.CheckResults.Select(r => r.Id).ToListAsync(Ct);
        Assert.Equivalent(new[] { boundary.Id, fresh.Id }, left);
    }

    [Fact]
    public async Task GetLast_and_GetRecent_return_newest_first()
    {
        var id = await AddMonitorAsync();
        var a = Success(id, T.AddMinutes(-3));
        var b = Failure(id, T.AddMinutes(-2));
        var c = Success(id, T.AddMinutes(-1));
        await SaveAsync(b, a, c);

        var last = await WithResults(r => r.GetLastAsync(id, Ct));
        var recent = await WithResults(r => r.GetRecentAsync(id, 2, Ct));

        Assert.Equal(c.Id, last?.Id);
        Assert.Equal([c.Id, b.Id], recent.Select(r => r.Id));
        Assert.Null(await WithResults(r => r.GetLastAsync(Guid.NewGuid(), Ct)));
    }
}
