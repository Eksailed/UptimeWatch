using Microsoft.EntityFrameworkCore;
using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Persistence;

public sealed class CheckResultRepository(AppDbContext db) : ICheckResultRepository
{
    public async Task<double?> GetUptimePercentAsync(Guid monitorId, DateTime since, CancellationToken ct)
    {
        since = UtcDateTime.Normalize(since);

        var stats = await db.CheckResults.AsNoTracking()
            .Where(r => r.MonitorId == monitorId && r.CheckedAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Successes = g.Count(r => r.Success) })
            .SingleOrDefaultAsync(ct); // one constant group at most: no rows in the window means no group at all

        return stats is null || stats.Total == 0 ? null : stats.Successes * 100.0 / stats.Total;
    }

    public Task<CheckResult?> GetLastAsync(Guid monitorId, CancellationToken ct) =>
        db.CheckResults.AsNoTracking()
            .Where(r => r.MonitorId == monitorId)
            .OrderByDescending(r => r.CheckedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<CheckResult>> GetRecentAsync(Guid monitorId, int count, CancellationToken ct) =>
        await db.CheckResults.AsNoTracking()
            .Where(r => r.MonitorId == monitorId)
            .OrderByDescending(r => r.CheckedAt)
            .Take(Math.Max(count, 0))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Incident>> GetIncidentsAsync(Guid monitorId, int count, CancellationToken ct) =>
        await db.Incidents.AsNoTracking()
            .Where(i => i.MonitorId == monitorId)
            .OrderByDescending(i => i.StartedAt)
            .Take(Math.Max(count, 0))
            .ToListAsync(ct);

    public Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct)
    {
        cutoff = UtcDateTime.Normalize(cutoff);
        return db.CheckResults.Where(r => r.CheckedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
