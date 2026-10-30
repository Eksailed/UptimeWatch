using Microsoft.EntityFrameworkCore;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

public sealed class DateTimeMappingTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task Times_round_trip_as_utc()
    {
        var at = new DateTime(2026, 10, 30, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);
        var monitor = NewMonitor(now: at);
        await WithMonitors(r => r.AddAsync(monitor, CancellationToken.None));
        var result = Success(monitor.Id, at);
        await WithMonitors(r => r.SaveCheckAsync(monitor, result, null, CancellationToken.None));

        var loaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", CancellationToken.None)))!;
        var last = (await WithResults(r => r.GetLastAsync(monitor.Id, CancellationToken.None)))!;

        Assert.Equal(at, loaded.NextCheckAt);
        Assert.Equal(DateTimeKind.Utc, loaded.NextCheckAt.Kind);
        Assert.Equal(at, last.CheckedAt);
        Assert.Equal(DateTimeKind.Utc, last.CheckedAt.Kind);
    }

    [Fact]
    public async Task Unspecified_and_local_times_are_stored_without_errors()
    {
        var utc = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        var local = utc.ToLocalTime();

        var a = NewMonitor("alice", "https://a.example.com", unspecified);
        var b = NewMonitor("alice", "https://b.example.com", local);
        await WithMonitors(r => r.AddAsync(a, CancellationToken.None));
        await WithMonitors(r => r.AddAsync(b, CancellationToken.None));

        var loadedA = (await WithMonitors(r => r.GetAsync(a.Id, "alice", CancellationToken.None)))!;
        var loadedB = (await WithMonitors(r => r.GetAsync(b.Id, "alice", CancellationToken.None)))!;

        Assert.Equal(utc, loadedA.NextCheckAt);
        Assert.Equal(utc, loadedB.NextCheckAt);
        Assert.Equal(DateTimeKind.Utc, loadedB.NextCheckAt.Kind);

        // Query parameters and ClaimDue arguments with a non-UTC kind must work as well.
        var claimed = await WithMonitors(r => r.ClaimDueAsync(local, 10, TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Equal(2, claimed.Count);
        Assert.Equal(utc.AddMinutes(1), claimed[0].NextCheckAt);
        await using var db = Postgres.CreateContext();
        Assert.Equal(2, await db.Monitors.CountAsync(m => m.NextCheckAt > unspecified));
    }
}
