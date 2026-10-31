using UptimeWatch.Domain;

namespace UptimeWatch.Application;

public sealed class MonitorService(IMonitorRepository monitors, ICheckResultRepository results, TimeProvider time)
{
    public async Task<OperationResult<MonitorStatusDto>> CreateAsync(
        string ownerId, string url, int intervalSeconds, int expectedStatusCode, CancellationToken ct)
    {
        var now = Now();
        SiteMonitor monitor;
        try
        {
            monitor = SiteMonitor.Create(ownerId, url?.Trim() ?? string.Empty, intervalSeconds, expectedStatusCode, now);
        }
        catch (DomainException ex)
        {
            return new OperationResult<MonitorStatusDto>(null, ex.Error);
        }

        if (await monitors.CountByOwnerAsync(ownerId, ct) >= MonitorLimits.MaxMonitorsPerUser)
        {
            return new OperationResult<MonitorStatusDto>(
                null,
                new ErrorInfo(
                    ErrorCode.MonitorLimitReached,
                    $"You can have at most {MonitorLimits.MaxMonitorsPerUser} monitors.",
                    MonitorLimits.MaxMonitorsPerUser));
        }

        await monitors.AddAsync(monitor, ct);
        var dto = await MonitorStatusBuilder.BuildAsync(monitor, results, now, null, ct);
        return new OperationResult<MonitorStatusDto>(dto, null);
    }

    public async Task<IReadOnlyList<MonitorStatusDto>> ListAsync(string ownerId, CancellationToken ct)
    {
        var now = Now();
        var list = await monitors.ListByOwnerAsync(ownerId, ct);
        var dtos = new List<MonitorStatusDto>(list.Count);
        foreach (var monitor in list)
        {
            dtos.Add(await MonitorStatusBuilder.BuildAsync(monitor, results, now, null, ct));
        }

        return dtos;
    }

    public async Task<MonitorStatusDto?> GetAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var monitor = await monitors.GetAsync(id, ownerId, ct);
        return monitor is null ? null : await MonitorStatusBuilder.BuildAsync(monitor, results, Now(), null, ct);
    }

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
        monitors.DeleteAsync(id, ownerId, ct);

    public async Task<bool> SetEnabledAsync(Guid id, string ownerId, bool enabled, CancellationToken ct)
    {
        var monitor = await monitors.GetAsync(id, ownerId, ct);
        if (monitor is null)
        {
            return false;
        }

        monitor.SetEnabled(enabled, Now());
        await monitors.UpdateAsync(monitor, ct);
        return true;
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
