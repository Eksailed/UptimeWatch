using UptimeWatch.Domain;

namespace UptimeWatch.Application;

public sealed record OperationResult<T>(T? Value, string? Error);

public sealed record UptimeDto(double? Last24h, double? Last7d, double? Last30d);

public sealed record MonitorStatusDto(
    Guid Id,
    string Url,
    int IntervalSeconds,
    int ExpectedStatusCode,
    bool Enabled,
    bool IsDown,
    int? LastStatusCode,
    int? LastResponseTimeMs,
    DateTime? LastCheckedAt,
    UptimeDto Uptime);

internal static class MonitorStatusBuilder
{
    /// <summary>
    /// Builds the status DTO. Pass <paramref name="lastResult"/> when it is already known
    /// (e.g. the check that was just saved); otherwise the latest result is loaded.
    /// </summary>
    public static async Task<MonitorStatusDto> BuildAsync(
        SiteMonitor monitor,
        ICheckResultRepository results,
        DateTime now,
        CheckResult? lastResult,
        CancellationToken ct)
    {
        var last = lastResult ?? await results.GetLastAsync(monitor.Id, ct);
        var uptime = new UptimeDto(
            await results.GetUptimePercentAsync(monitor.Id, now.AddHours(-24), ct),
            await results.GetUptimePercentAsync(monitor.Id, now.AddDays(-7), ct),
            await results.GetUptimePercentAsync(monitor.Id, now.AddDays(-30), ct));

        return new MonitorStatusDto(
            monitor.Id,
            monitor.Url,
            monitor.IntervalSeconds,
            monitor.ExpectedStatusCode,
            monitor.Enabled,
            monitor.OpenIncident is not null,
            last?.StatusCode,
            last?.ResponseTimeMs,
            last?.CheckedAt,
            uptime);
    }
}
