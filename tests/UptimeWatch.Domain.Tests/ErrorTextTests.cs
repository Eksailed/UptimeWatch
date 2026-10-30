namespace UptimeWatch.Domain.Tests;

public class ErrorTextTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Short_and_null_errors_are_unchanged()
    {
        Assert.Null(ErrorText.Truncate(null));
        Assert.Equal("boom", ErrorText.Truncate("boom"));
        var exact = new string('x', MonitorLimits.MaxErrorLength);
        Assert.Equal(exact, ErrorText.Truncate(exact));
    }

    [Fact]
    public void Long_error_is_cut_to_the_limit_with_an_ellipsis()
    {
        var truncated = ErrorText.Truncate(new string('x', 5000))!;

        Assert.Equal(MonitorLimits.MaxErrorLength, truncated.Length);
        Assert.EndsWith("…", truncated);
    }

    [Fact]
    public void Truncation_does_not_split_a_surrogate_pair()
    {
        var truncated = ErrorText.Truncate(string.Concat(Enumerable.Repeat("😀", 2000)))!;

        Assert.True(truncated.Length <= MonitorLimits.MaxErrorLength);
        Assert.EndsWith("…", truncated);
        Assert.False(char.IsHighSurrogate(truncated[^2]));
    }

    [Fact]
    public void Check_result_and_incident_store_the_truncated_error()
    {
        var monitor = SiteMonitor.Create("u", "https://example.com", 60, 200, T0);
        var first = CheckResult.Create(monitor.Id, T0, false, null, 10, new string('a', 5000));
        Assert.Equal(MonitorLimits.MaxErrorLength, first.Error!.Length);

        monitor.ApplyResult(first, T0);
        monitor.ApplyResult(CheckResult.Create(monitor.Id, T0, false, null, 10, new string('b', 5000)), T0);
        monitor.ApplyResult(CheckResult.Create(monitor.Id, T0, false, null, 10, new string('c', 5000)), T0);
        Assert.Equal(MonitorLimits.MaxErrorLength, monitor.OpenIncident!.LastError!.Length);

        // A further failure updates the open incident's error, which is bounded too.
        monitor.ApplyResult(CheckResult.Create(monitor.Id, T0, false, null, 10, new string('d', 5000)), T0);
        Assert.Equal(MonitorLimits.MaxErrorLength, monitor.OpenIncident!.LastError!.Length);
        Assert.StartsWith("ddd", monitor.OpenIncident.LastError);
    }
}
