using UptimeWatch.Domain;

namespace UptimeWatch.Application;

public interface IMonitorRepository
{
    Task AddAsync(SiteMonitor m, CancellationToken ct);

    Task<SiteMonitor?> GetAsync(Guid id, string ownerId, CancellationToken ct);

    Task<IReadOnlyList<SiteMonitor>> ListByOwnerAsync(string ownerId, CancellationToken ct);

    Task<int> CountByOwnerAsync(string ownerId, CancellationToken ct);

    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct);

    Task UpdateAsync(SiteMonitor m, CancellationToken ct);

    /// <summary>
    /// Atomically selects enabled monitors with NextCheckAt &lt;= now and moves their NextCheckAt to now + lease.
    /// </summary>
    Task<IReadOnlyList<SiteMonitor>> ClaimDueAsync(DateTime now, int limit, TimeSpan lease, CancellationToken ct);

    /// <summary>
    /// Persists the outcome of one check in a single transaction. Returns false if the monitor no longer exists
    /// OR a stale copy of the monitor conflicts with a newer incident state (incident writes are fenced with
    /// <c>EndedAt IS NULL</c>). On a false result nothing is stored and the caller must not send any notification.
    /// Implementers must:
    /// <list type="bullet">
    /// <item>insert <paramref name="result"/>;</item>
    /// <item>save the monitor state (NextCheckAt, ConsecutiveFailures);</item>
    /// <item>persist <paramref name="closedIncident"/> (when not null) as an update of its EndedAt: after a
    /// close transition <see cref="SiteMonitor.OpenIncident"/> is already null, so the caller passes the
    /// incident that was open before the check;</item>
    /// <item>insert a new, not yet persisted <see cref="SiteMonitor.OpenIncident"/> (when not null);</item>
    /// <item>save an already persisted open incident too, because <see cref="Incident.LastError"/> is
    /// updated by later failures while the incident stays open.</item>
    /// </list>
    /// </summary>
    Task<bool> SaveCheckAsync(SiteMonitor monitor, CheckResult result, Incident? closedIncident, CancellationToken ct);
}

public interface ICheckResultRepository
{
    /// <summary>Uptime percentage since the given moment, or null if there were no checks.</summary>
    Task<double?> GetUptimePercentAsync(Guid monitorId, DateTime since, CancellationToken ct);

    Task<CheckResult?> GetLastAsync(Guid monitorId, CancellationToken ct);

    Task<IReadOnlyList<CheckResult>> GetRecentAsync(Guid monitorId, int count, CancellationToken ct);

    Task<IReadOnlyList<Incident>> GetIncidentsAsync(Guid monitorId, int count, CancellationToken ct);

    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken ct);
}

/// <summary>Performs one HTTP check. Must not throw on network errors; those are reported in the result.</summary>
public interface IHealthChecker
{
    Task<CheckResult> CheckAsync(SiteMonitor monitor, CancellationToken ct);
}

public interface INotifier
{
    Task NotifyDownAsync(SiteMonitor monitor, Incident incident, CancellationToken ct);

    Task NotifyRecoveredAsync(SiteMonitor monitor, Incident incident, CancellationToken ct);
}

public interface IMonitorNotifications
{
    Task PublishAsync(string ownerId, MonitorStatusDto status, CancellationToken ct);
}

public interface IUserProfileStore
{
    Task<string?> GetTelegramChatIdAsync(string userId, CancellationToken ct);

    Task SetTelegramChatIdAsync(string userId, string? chatId, CancellationToken ct);
}

public interface ICurrentUser
{
    string UserId { get; }
}
