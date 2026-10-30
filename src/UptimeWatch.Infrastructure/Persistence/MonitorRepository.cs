using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Persistence;

/// <summary>
/// Repository over <see cref="AppDbContext"/>. Entities cross DI scopes (claimed in one scope, saved in another),
/// so reads return detached entities and writes use explicit statements/states instead of change tracking.
/// </summary>
public sealed class MonitorRepository(AppDbContext db) : IMonitorRepository
{
    private const string ClaimSql = """
        WITH due AS (
            SELECT "Id", "NextCheckAt" AS "DueAt"
            FROM "Monitors"
            WHERE "Enabled" AND "NextCheckAt" <= @now
            ORDER BY "NextCheckAt"
            LIMIT @limit
            FOR UPDATE SKIP LOCKED
        ),
        claimed AS (
            UPDATE "Monitors" AS m
            SET "NextCheckAt" = @leaseUntil
            FROM due
            WHERE m."Id" = due."Id"
            RETURNING m."Id", due."DueAt"
        )
        SELECT "Id" FROM claimed ORDER BY "DueAt", "Id"
        """;

    public async Task AddAsync(SiteMonitor m, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(m);

        var entry = db.Monitors.Add(m);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<SiteMonitor?> GetAsync(Guid id, string ownerId, CancellationToken ct)
    {
        var monitor = await db.Monitors.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, ct);
        if (monitor is not null)
        {
            await AttachOpenIncidentsAsync([monitor], ct);
        }

        return monitor;
    }

    public async Task<IReadOnlyList<SiteMonitor>> ListByOwnerAsync(string ownerId, CancellationToken ct)
    {
        var monitors = await db.Monitors.AsNoTracking()
            .Where(m => m.OwnerId == ownerId)
            .OrderBy(m => m.Url).ThenBy(m => m.Id)
            .ToListAsync(ct);
        await AttachOpenIncidentsAsync(monitors, ct);
        return monitors;
    }

    public Task<int> CountByOwnerAsync(string ownerId, CancellationToken ct) =>
        db.Monitors.CountAsync(m => m.OwnerId == ownerId, ct);

    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
        await db.Monitors.Where(m => m.Id == id && m.OwnerId == ownerId).ExecuteDeleteAsync(ct) > 0;

    /// <summary>
    /// Saves user-editable settings. ConsecutiveFailures and incidents are owned by the check pipeline
    /// (<see cref="SaveCheckAsync"/>) and are deliberately not overwritten by a possibly stale entity.
    /// </summary>
    public async Task UpdateAsync(SiteMonitor m, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(m);

        await db.Monitors.Where(x => x.Id == m.Id && x.OwnerId == m.OwnerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Url, m.Url)
                .SetProperty(x => x.IntervalSeconds, m.IntervalSeconds)
                .SetProperty(x => x.ExpectedStatusCode, m.ExpectedStatusCode)
                .SetProperty(x => x.Enabled, m.Enabled)
                // An already enabled monitor may hold a lease in NextCheckAt; only enabling a disabled one reschedules it.
                .SetProperty(x => x.NextCheckAt, x => x.Enabled ? x.NextCheckAt : m.NextCheckAt), ct);
    }

    public async Task<IReadOnlyList<SiteMonitor>> ClaimDueAsync(DateTime now, int limit, TimeSpan lease, CancellationToken ct)
    {
        if (limit <= 0)
        {
            return [];
        }

        now = UtcDateTime.Normalize(now);
        var ids = new List<Guid>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            var transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            await using var command = new NpgsqlCommand(ClaimSql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now });
            command.Parameters.Add(new NpgsqlParameter("leaseUntil", NpgsqlDbType.TimestampTz) { Value = now + lease });
            command.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit });

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                ids.Add(reader.GetGuid(0));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (ids.Count == 0)
        {
            return [];
        }

        // The rows are leased to this caller now, so a plain read is safe.
        var loaded = await db.Monitors.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);
        await AttachOpenIncidentsAsync(loaded, ct);

        var byId = loaded.ToDictionary(m => m.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    public async Task<bool> SaveCheckAsync(SiteMonitor monitor, CheckResult result, Incident? closedIncident, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(result);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Updating the monitor row first also locks it, so a concurrent delete waits for this transaction.
        var updated = await db.Monitors.Where(m => m.Id == monitor.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ConsecutiveFailures, monitor.ConsecutiveFailures)
                .SetProperty(m => m.NextCheckAt, monitor.NextCheckAt), ct);
        if (updated == 0)
        {
            return false;
        }

        var entries = new List<EntityEntry> { db.CheckResults.Add(result) };
        try
        {
            // A false here means a stale copy lost against the stored incident state: nothing is persisted.
            if (closedIncident is not null && !await TryWriteIncidentAsync(closedIncident, closing: true, entries, ct))
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            if (monitor.OpenIncident is { } open
                && open.Id != closedIncident?.Id
                && !await TryWriteIncidentAsync(open, closing: false, entries, ct))
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Another open incident already exists for this monitor (stale copy).
                await transaction.RollbackAsync(ct);
                return false;
            }

            await transaction.CommitAsync(ct);
            return true;
        }
        finally
        {
            foreach (var entry in entries)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    /// <summary>
    /// Updates only a still-open incident row (EndedAt IS NULL) so a stale copy can neither re-open nor re-close one.
    /// Inserts the incident only if no row with this id exists. Returns false on a conflict.
    /// </summary>
    private async Task<bool> TryWriteIncidentAsync(Incident incident, bool closing, List<EntityEntry> entries, CancellationToken ct)
    {
        var openRows = db.Incidents.Where(i => i.Id == incident.Id && i.EndedAt == null);
        var updated = closing
            ? await openRows.ExecuteUpdateAsync(s => s
                .SetProperty(i => i.EndedAt, incident.EndedAt)
                .SetProperty(i => i.LastError, incident.LastError), ct)
            : await openRows.ExecuteUpdateAsync(s => s.SetProperty(i => i.LastError, incident.LastError), ct);
        if (updated > 0)
        {
            return true;
        }

        if (await db.Incidents.AnyAsync(i => i.Id == incident.Id, ct))
        {
            return false;
        }

        entries.Add(db.Incidents.Add(incident));
        return true;
    }

    private async Task AttachOpenIncidentsAsync(IReadOnlyCollection<SiteMonitor> monitors, CancellationToken ct)
    {
        if (monitors.Count == 0)
        {
            return;
        }

        var ids = monitors.Select(m => m.Id).ToList();
        var open = await db.Incidents.AsNoTracking()
            .Where(i => i.EndedAt == null && ids.Contains(i.MonitorId))
            .ToListAsync(ct);
        var byMonitor = open.ToDictionary(i => i.MonitorId);

        foreach (var monitor in monitors)
        {
            if (byMonitor.TryGetValue(monitor.Id, out var incident))
            {
                MonitorAccessors.SetOpenIncident(monitor, incident);
            }
        }
    }
}
