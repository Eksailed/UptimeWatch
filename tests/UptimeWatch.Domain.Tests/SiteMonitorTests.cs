namespace UptimeWatch.Domain.Tests;

public class SiteMonitorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static SiteMonitor NewMonitor(int interval = 60) =>
        SiteMonitor.Create("user-1", "https://example.com", interval, 200, T0);

    private static CheckResult Fail(SiteMonitor m, DateTime at, string error = "timeout") =>
        CheckResult.Create(m.Id, at, false, null, 1000, error);

    private static CheckResult Ok(SiteMonitor m, DateTime at) =>
        CheckResult.Create(m.Id, at, true, 200, 120, null);

    private static void Apply(SiteMonitor m, CheckResult r, DateTime now) => m.ApplyResult(r, now);

    [Fact]
    public void Create_rejects_interval_below_30_seconds()
    {
        var ex = Assert.Throws<DomainException>(() => SiteMonitor.Create("u", "https://example.com", 29, 200, T0));
        Assert.Equal(ErrorCode.IntervalTooShort, ex.Error.Code);
        Assert.Equal([MonitorLimits.MinIntervalSeconds], ex.Error.Args);
        var monitor = SiteMonitor.Create("u", "https://example.com", 30, 200, T0);
        Assert.Equal(30, monitor.IntervalSeconds);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Create_rejects_expected_status_outside_100_599(int status)
    {
        var ex = Assert.Throws<DomainException>(() => SiteMonitor.Create("u", "https://example.com", 60, status, T0));
        Assert.Equal(ErrorCode.ExpectedStatusOutOfRange, ex.Error.Code);
        Assert.Equal([100, 599], ex.Error.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_owner(string owner)
    {
        var ex = Assert.Throws<DomainException>(() => SiteMonitor.Create(owner, "https://example.com", 60, 200, T0));
        Assert.Equal(ErrorCode.OwnerRequired, ex.Error.Code);
    }

    [Fact]
    public void Create_rejects_url_that_fails_UrlPolicy()
    {
        var ex = Assert.Throws<DomainException>(() => SiteMonitor.Create("u", "http://localhost", 60, 200, T0));
        Assert.False(UrlPolicy.TryValidate("http://localhost", out var error));
        Assert.Equal(error, ex.Error);
        Assert.Equal(error!.Message, ex.Message);
    }

    [Fact]
    public void Create_sets_defaults_and_is_due_immediately()
    {
        var m = NewMonitor();
        Assert.NotEqual(Guid.Empty, m.Id);
        Assert.True(m.Enabled);
        Assert.Equal(T0, m.NextCheckAt);
        Assert.Equal(0, m.ConsecutiveFailures);
        Assert.Null(m.OpenIncident);
    }

    [Fact]
    public void ApplyResult_two_failures_do_not_open_incident()
    {
        var m = NewMonitor();
        Assert.Equal(IncidentTransition.None, m.ApplyResult(Fail(m, T0), T0));
        Assert.Equal(IncidentTransition.None, m.ApplyResult(Fail(m, T0.AddSeconds(60)), T0.AddSeconds(60)));
        Assert.Null(m.OpenIncident);
    }

    [Fact]
    public void ApplyResult_third_consecutive_failure_opens_incident()
    {
        var m = NewMonitor();
        Apply(m, Fail(m, T0), T0);
        Apply(m, Fail(m, T0.AddSeconds(60)), T0.AddSeconds(60));
        var now = T0.AddSeconds(120);

        var transition = m.ApplyResult(Fail(m, now, "HTTP 500"), now);

        Assert.Equal(IncidentTransition.Opened, transition);
        Assert.NotNull(m.OpenIncident);
        Assert.Equal(now, m.OpenIncident!.StartedAt);
        Assert.Equal("HTTP 500", m.OpenIncident.LastError);
        Assert.Equal(m.Id, m.OpenIncident.MonitorId);
        Assert.Null(m.OpenIncident.EndedAt);
    }

    [Fact]
    public void ApplyResult_success_between_failures_resets_counter()
    {
        var m = NewMonitor();
        Apply(m, Fail(m, T0), T0);
        Apply(m, Fail(m, T0.AddSeconds(60)), T0.AddSeconds(60));
        Apply(m, Ok(m, T0.AddSeconds(120)), T0.AddSeconds(120));
        Apply(m, Fail(m, T0.AddSeconds(180)), T0.AddSeconds(180));
        Apply(m, Fail(m, T0.AddSeconds(240)), T0.AddSeconds(240));

        Assert.Null(m.OpenIncident);
        Assert.Equal(2, m.ConsecutiveFailures);
    }

    [Fact]
    public void ApplyResult_fourth_failure_returns_None_while_incident_open()
    {
        var m = NewMonitor();
        for (var i = 0; i < 3; i++) Apply(m, Fail(m, T0.AddSeconds(60 * i)), T0.AddSeconds(60 * i));
        var now = T0.AddSeconds(180);

        var transition = m.ApplyResult(Fail(m, now, "still down"), now);

        Assert.Equal(IncidentTransition.None, transition);
        Assert.NotNull(m.OpenIncident);
        Assert.Equal(T0.AddSeconds(120), m.OpenIncident!.StartedAt);
        Assert.Equal("still down", m.OpenIncident.LastError);
    }

    [Fact]
    public void ApplyResult_success_closes_incident()
    {
        var m = NewMonitor();
        for (var i = 0; i < 3; i++) Apply(m, Fail(m, T0.AddSeconds(60 * i)), T0.AddSeconds(60 * i));
        var opened = m.OpenIncident!;
        var now = T0.AddSeconds(300);

        var transition = m.ApplyResult(Ok(m, now), now);

        Assert.Equal(IncidentTransition.Closed, transition);
        Assert.Equal(now, opened.EndedAt);
        Assert.Equal(now - opened.StartedAt, opened.Duration);
        Assert.Null(m.OpenIncident);
        Assert.Equal(0, m.ConsecutiveFailures);
    }

    [Fact]
    public void ApplyResult_success_without_incident_returns_None()
    {
        var m = NewMonitor();
        Assert.Equal(IncidentTransition.None, m.ApplyResult(Ok(m, T0), T0));
    }

    [Fact]
    public void Incident_open_has_no_duration()
    {
        var m = NewMonitor();
        for (var i = 0; i < 3; i++) Apply(m, Fail(m, T0.AddSeconds(60 * i)), T0.AddSeconds(60 * i));
        Assert.Null(m.OpenIncident!.Duration);
    }

    [Fact]
    public void ApplyResult_sets_NextCheckAt_to_now_plus_interval()
    {
        var m = NewMonitor(interval: 45);
        var now = T0.AddSeconds(10);

        m.ApplyResult(Ok(m, now), now);

        Assert.Equal(now.AddSeconds(45), m.NextCheckAt);
    }

    [Fact]
    public void SetEnabled_true_makes_monitor_due_immediately()
    {
        var m = NewMonitor();
        m.SetEnabled(false, T0);
        var later = T0.AddHours(1);

        m.SetEnabled(true, later);

        Assert.True(m.Enabled);
        Assert.Equal(later, m.NextCheckAt);
    }
}
