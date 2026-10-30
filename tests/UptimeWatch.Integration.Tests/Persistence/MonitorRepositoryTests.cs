using Microsoft.EntityFrameworkCore;
using UptimeWatch.Domain;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

public sealed class MonitorRepositoryTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Get_filters_by_owner()
    {
        var mine = NewMonitor("alice", "https://a.example.com");
        var theirs = NewMonitor("bob", "https://b.example.com");
        await WithMonitors(r => r.AddAsync(mine, Ct));
        await WithMonitors(r => r.AddAsync(theirs, Ct));

        var found = await WithMonitors(r => r.GetAsync(mine.Id, "alice", Ct));
        var foreign = await WithMonitors(r => r.GetAsync(theirs.Id, "alice", Ct));
        var list = await WithMonitors(r => r.ListByOwnerAsync("alice", Ct));
        var count = await WithMonitors(r => r.CountByOwnerAsync("alice", Ct));

        Assert.NotNull(found);
        Assert.Equal("https://a.example.com", found.Url);
        Assert.Equal(60, found.IntervalSeconds);
        Assert.Equal(200, found.ExpectedStatusCode);
        Assert.True(found.Enabled);
        Assert.Null(foreign);
        Assert.Equal([mine.Id], list.Select(m => m.Id));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ClaimDue_returns_only_enabled_monitors_with_NextCheckAt_in_the_past_ordered_by_NextCheckAt()
    {
        var later = NewMonitor(url: "https://later.example.com", now: T.AddMinutes(-5));
        var earlier = NewMonitor(url: "https://earlier.example.com", now: T.AddMinutes(-10));
        var future = NewMonitor(url: "https://future.example.com", now: T.AddMinutes(1));
        var disabled = NewMonitor(url: "https://disabled.example.com", now: T.AddMinutes(-20));
        disabled.SetEnabled(false, T);
        foreach (var m in new[] { later, future, disabled, earlier })
        {
            await WithMonitors(r => r.AddAsync(m, Ct));
        }

        var claimed = await WithMonitors(r => r.ClaimDueAsync(T, 10, TimeSpan.FromMinutes(1), Ct));

        Assert.Equal([earlier.Id, later.Id], claimed.Select(m => m.Id));
    }

    [Fact]
    public async Task ClaimDue_respects_the_limit_and_takes_the_most_overdue_first()
    {
        var ms = Enumerable.Range(1, 5).Select(i => NewMonitor(url: $"https://m{i}.example.com", now: T.AddMinutes(-i))).ToList();
        foreach (var m in ms)
        {
            await WithMonitors(r => r.AddAsync(m, Ct));
        }

        var claimed = await WithMonitors(r => r.ClaimDueAsync(T, 2, TimeSpan.FromMinutes(1), Ct));

        Assert.Equal([ms[4].Id, ms[3].Id], claimed.Select(m => m.Id));
    }

    [Fact]
    public async Task ClaimDue_called_twice_returns_each_monitor_once()
    {
        const int total = 60;
        for (var i = 0; i < total; i++)
        {
            var m = NewMonitor(url: $"https://m{i}.example.com", now: T.AddSeconds(-i - 1));
            await WithMonitors(r => r.AddAsync(m, Ct));
        }

        var batches = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => WithMonitors(r => r.ClaimDueAsync(T, 20, TimeSpan.FromMinutes(1), Ct))));

        var ids = batches.SelectMany(b => b.Select(m => m.Id)).ToList();
        Assert.All(batches, b => Assert.True(b.Count <= 20));
        Assert.Equal(total, ids.Count);
        Assert.Equal(total, ids.Distinct().Count());
    }

    [Fact]
    public async Task ClaimDue_monitor_becomes_due_again_after_lease_expires()
    {
        var monitor = NewMonitor(now: T.AddMinutes(-1));
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        var lease = TimeSpan.FromSeconds(60);

        var first = await WithMonitors(r => r.ClaimDueAsync(T, 10, lease, Ct));
        var during = await WithMonitors(r => r.ClaimDueAsync(T.AddSeconds(30), 10, lease, Ct));
        var after = await WithMonitors(r => r.ClaimDueAsync(T.AddSeconds(61), 10, lease, Ct));

        Assert.Equal([monitor.Id], first.Select(m => m.Id));
        Assert.Equal(T.AddSeconds(60), first[0].NextCheckAt);
        Assert.Empty(during);
        Assert.Equal([monitor.Id], after.Select(m => m.Id));
    }

    [Fact]
    public async Task ClaimDue_loads_the_open_incident()
    {
        var monitor = await AddMonitorWithOpenIncident();

        var claimed = await WithMonitors(r => r.ClaimDueAsync(T.AddDays(1), 10, TimeSpan.FromMinutes(1), Ct));

        var loaded = Assert.Single(claimed);
        Assert.Equal(monitor.OpenIncident!.Id, loaded.OpenIncident?.Id);
        Assert.Equal(MonitorLimits.FailureThreshold, loaded.ConsecutiveFailures);
    }

    [Fact]
    public async Task SaveCheck_persists_result_state_and_opened_incident_together()
    {
        var monitor = NewMonitor();
        await WithMonitors(r => r.AddAsync(monitor, Ct));

        // Claimed in one context/scope, processed and saved in others.
        var claimed = (await WithMonitors(r => r.ClaimDueAsync(T, 10, TimeSpan.FromMinutes(1), Ct))).Single();
        CheckResult? last = null;
        for (var i = 0; i < MonitorLimits.FailureThreshold; i++)
        {
            var at = T.AddMinutes(i + 1);
            last = Failure(claimed.Id, at, $"error {i}");
            claimed.ApplyResult(last, at);
            var current = last;
            Assert.True(await WithMonitors(r => r.SaveCheckAsync(claimed, current, null, Ct)));
        }

        var loaded = await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct));
        var results = await WithResults(r => r.GetRecentAsync(monitor.Id, 10, Ct));
        var incidents = await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct));

        Assert.NotNull(loaded);
        Assert.Equal(MonitorLimits.FailureThreshold, loaded.ConsecutiveFailures);
        Assert.Equal(claimed.NextCheckAt, loaded.NextCheckAt);
        Assert.Equal(3, results.Count);
        Assert.Equal(last!.Id, results[0].Id);
        Assert.Equal("error 2", results[0].Error);
        var incident = Assert.Single(incidents);
        Assert.Equal(claimed.OpenIncident!.Id, incident.Id);
        Assert.Null(incident.EndedAt);
        Assert.Equal(loaded.OpenIncident!.Id, incident.Id);
    }

    [Fact]
    public async Task SaveCheck_updates_LastError_of_an_already_persisted_open_incident()
    {
        var monitor = await AddMonitorWithOpenIncident("first");
        var loaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;

        var result = Failure(loaded.Id, T.AddMinutes(10), "second");
        loaded.ApplyResult(result, T.AddMinutes(10));
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(loaded, result, null, Ct)));

        var incidents = await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct));
        var incident = Assert.Single(incidents);
        Assert.Equal("second", incident.LastError);
        Assert.Null(incident.EndedAt);
        Assert.Equal(4, (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!.ConsecutiveFailures);
    }

    [Fact]
    public async Task SaveCheck_closes_incident_and_stores_EndedAt()
    {
        var monitor = await AddMonitorWithOpenIncident();
        var loaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;

        var recoveredAt = T.AddMinutes(30);
        var result = Success(loaded.Id, recoveredAt);
        var openBefore = loaded.OpenIncident;
        var transition = loaded.ApplyResult(result, recoveredAt);
        Assert.Equal(IncidentTransition.Closed, transition);
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(loaded, result, openBefore, Ct)));

        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var incident = Assert.Single(await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct)));
        Assert.Null(reloaded.OpenIncident);
        Assert.Equal(0, reloaded.ConsecutiveFailures);
        Assert.Equal(recoveredAt, incident.EndedAt);
        Assert.Equal(DateTimeKind.Utc, incident.EndedAt!.Value.Kind);
        Assert.Equal(recoveredAt - incident.StartedAt, incident.Duration);
    }

    [Fact]
    public async Task SaveCheck_allows_a_new_incident_after_the_previous_one_was_closed()
    {
        var monitor = await AddMonitorWithOpenIncident();
        var loaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var ok = Success(loaded.Id, T.AddMinutes(30));
        var openBefore = loaded.OpenIncident;
        loaded.ApplyResult(ok, T.AddMinutes(30));
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(loaded, ok, openBefore, Ct)));

        for (var i = 0; i < MonitorLimits.FailureThreshold; i++)
        {
            var at = T.AddMinutes(40 + i);
            var fail = Failure(loaded.Id, at);
            loaded.ApplyResult(fail, at);
            Assert.True(await WithMonitors(r => r.SaveCheckAsync(loaded, fail, null, Ct)));
        }

        var incidents = await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct));
        Assert.Equal(2, incidents.Count);
        Assert.Single(incidents, i => i.EndedAt is null);
    }

    [Fact]
    public async Task SaveCheck_returns_false_when_monitor_was_deleted()
    {
        var monitor = NewMonitor();
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        var claimed = (await WithMonitors(r => r.ClaimDueAsync(T, 10, TimeSpan.FromMinutes(1), Ct))).Single();
        Assert.True(await WithMonitors(r => r.DeleteAsync(monitor.Id, "alice", Ct)));

        var result = Failure(claimed.Id, T.AddSeconds(5));
        claimed.ApplyResult(result, T.AddSeconds(5));
        var saved = await WithMonitors(r => r.SaveCheckAsync(claimed, result, null, Ct));

        Assert.False(saved);
        await using var db = Postgres.CreateContext();
        Assert.Equal(0, await db.CheckResults.CountAsync());
        Assert.Equal(0, await db.Monitors.CountAsync());
    }

    [Fact]
    public async Task Delete_cascades_results_and_incidents()
    {
        var monitor = await AddMonitorWithOpenIncident();
        var other = NewMonitor("bob", "https://other.example.com");
        await WithMonitors(r => r.AddAsync(other, Ct));
        var otherResult = Success(other.Id, T);
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(other, otherResult, null, Ct)));

        var deleted = await WithMonitors(r => r.DeleteAsync(monitor.Id, "alice", Ct));

        Assert.True(deleted);
        await using var db = Postgres.CreateContext();
        Assert.Empty(await db.Monitors.Where(m => m.Id == monitor.Id).ToListAsync());
        Assert.Empty(await db.CheckResults.Where(r => r.MonitorId == monitor.Id).ToListAsync());
        Assert.Empty(await db.Incidents.Where(i => i.MonitorId == monitor.Id).ToListAsync());
        Assert.Single(await db.CheckResults.ToListAsync());
    }

    [Fact]
    public async Task Delete_by_other_owner_returns_false()
    {
        var monitor = NewMonitor("alice");
        await WithMonitors(r => r.AddAsync(monitor, Ct));

        var deleted = await WithMonitors(r => r.DeleteAsync(monitor.Id, "mallory", Ct));

        Assert.False(deleted);
        Assert.NotNull(await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)));
    }

    [Fact]
    public async Task Update_persists_enabled_flag_but_does_not_overwrite_failure_counter()
    {
        var monitor = await AddMonitorWithOpenIncident();

        // A copy loaded earlier than the final state; its failure counter must not be written back.
        var copy = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var more = Failure(monitor.Id, T.AddMinutes(20));
        monitor.ApplyResult(more, T.AddMinutes(20));
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(monitor, more, null, Ct)));

        copy.SetEnabled(false, T.AddHours(1));
        await WithMonitors(r => r.UpdateAsync(copy, Ct));

        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        Assert.False(reloaded.Enabled);
        Assert.Equal(MonitorLimits.FailureThreshold + 1, reloaded.ConsecutiveFailures);
        Assert.NotNull(reloaded.OpenIncident);
    }

    [Fact]
    public async Task SaveCheck_stale_copy_cannot_reopen_or_update_a_closed_incident()
    {
        var monitor = await AddMonitorWithOpenIncident();
        var stale = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var fresh = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;

        var recoveredAt = T.AddMinutes(30);
        var ok = Success(fresh.Id, recoveredAt);
        var openBefore = fresh.OpenIncident;
        fresh.ApplyResult(ok, recoveredAt);
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(fresh, ok, openBefore, Ct)));

        var staleFailure = Failure(stale.Id, T.AddMinutes(31), "late");
        stale.ApplyResult(staleFailure, T.AddMinutes(31));
        var saved = await WithMonitors(r => r.SaveCheckAsync(stale, staleFailure, null, Ct));

        Assert.False(saved);
        var incident = Assert.Single(await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct)));
        Assert.Equal(recoveredAt, incident.EndedAt);
        Assert.NotEqual("late", incident.LastError);
        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        Assert.Equal(0, reloaded.ConsecutiveFailures);
        Assert.Null(reloaded.OpenIncident);
        var results = await WithResults(r => r.GetRecentAsync(monitor.Id, 20, Ct));
        Assert.DoesNotContain(results, r => r.Id == staleFailure.Id);
        Assert.Equal(MonitorLimits.FailureThreshold + 1, results.Count);
    }

    [Fact]
    public async Task SaveCheck_second_close_of_the_same_incident_returns_false_and_keeps_EndedAt()
    {
        var monitor = await AddMonitorWithOpenIncident();
        var first = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var second = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;

        var firstAt = T.AddMinutes(30);
        var firstOk = Success(first.Id, firstAt);
        var firstOpen = first.OpenIncident;
        first.ApplyResult(firstOk, firstAt);
        Assert.True(await WithMonitors(r => r.SaveCheckAsync(first, firstOk, firstOpen, Ct)));

        var secondAt = T.AddMinutes(45);
        var secondOk = Success(second.Id, secondAt);
        var secondOpen = second.OpenIncident;
        second.ApplyResult(secondOk, secondAt);
        var saved = await WithMonitors(r => r.SaveCheckAsync(second, secondOk, secondOpen, Ct));

        Assert.False(saved);
        var incident = Assert.Single(await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct)));
        Assert.Equal(firstAt, incident.EndedAt);
        var results = await WithResults(r => r.GetRecentAsync(monitor.Id, 20, Ct));
        Assert.DoesNotContain(results, r => r.Id == secondOk.Id);
        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        Assert.Equal(firstAt.AddSeconds(60), reloaded.NextCheckAt);
    }

    [Fact]
    public async Task SaveCheck_stale_copy_cannot_open_a_second_incident()
    {
        var monitor = NewMonitor();
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        for (var i = 0; i < MonitorLimits.FailureThreshold - 1; i++)
        {
            var at = T.AddMinutes(i + 1);
            var fail = Failure(monitor.Id, at);
            monitor.ApplyResult(fail, at);
            Assert.True(await WithMonitors(r => r.SaveCheckAsync(monitor, fail, null, Ct)));
        }

        // Two workers hold copies with 2 failures; both see the 3rd failure and try to open an incident.
        var a = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var b = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var resultA = Failure(a.Id, T.AddMinutes(10));
        var resultB = Failure(b.Id, T.AddMinutes(10));
        Assert.Equal(IncidentTransition.Opened, a.ApplyResult(resultA, T.AddMinutes(10)));
        Assert.Equal(IncidentTransition.Opened, b.ApplyResult(resultB, T.AddMinutes(10)));

        Assert.True(await WithMonitors(r => r.SaveCheckAsync(a, resultA, null, Ct)));
        Assert.False(await WithMonitors(r => r.SaveCheckAsync(b, resultB, null, Ct)));

        var incident = Assert.Single(await WithResults(r => r.GetIncidentsAsync(monitor.Id, 10, Ct)));
        Assert.Equal(a.OpenIncident!.Id, incident.Id);
        var results = await WithResults(r => r.GetRecentAsync(monitor.Id, 20, Ct));
        Assert.DoesNotContain(results, r => r.Id == resultB.Id);
    }

    [Fact]
    public async Task Update_keeps_the_leased_NextCheckAt_of_an_enabled_monitor()
    {
        var monitor = NewMonitor(now: T.AddMinutes(-1));
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        var copy = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        var claimed = (await WithMonitors(r => r.ClaimDueAsync(T, 10, TimeSpan.FromMinutes(1), Ct))).Single();
        Assert.Equal(T.AddMinutes(1), claimed.NextCheckAt);

        copy.SetEnabled(true, T.AddHours(5));
        await WithMonitors(r => r.UpdateAsync(copy, Ct));

        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        Assert.True(reloaded.Enabled);
        Assert.Equal(T.AddMinutes(1), reloaded.NextCheckAt);
    }

    [Fact]
    public async Task Update_sets_NextCheckAt_when_a_disabled_monitor_is_enabled()
    {
        var monitor = NewMonitor();
        await WithMonitors(r => r.AddAsync(monitor, Ct));
        var disabled = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        disabled.SetEnabled(false, T);
        await WithMonitors(r => r.UpdateAsync(disabled, Ct));

        var enabling = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        enabling.SetEnabled(true, T.AddHours(2));
        await WithMonitors(r => r.UpdateAsync(enabling, Ct));

        var reloaded = (await WithMonitors(r => r.GetAsync(monitor.Id, "alice", Ct)))!;
        Assert.True(reloaded.Enabled);
        Assert.Equal(T.AddHours(2), reloaded.NextCheckAt);
    }
}
