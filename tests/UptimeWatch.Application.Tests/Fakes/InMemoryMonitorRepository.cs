using UptimeWatch.Application;
using UptimeWatch.Domain;

namespace UptimeWatch.Application.Tests.Fakes;

public sealed class InMemoryMonitorRepository : IMonitorRepository
{
    private readonly List<SiteMonitor> _items = [];

    public IReadOnlyList<SiteMonitor> Items => _items;

    public Task AddAsync(SiteMonitor m, CancellationToken ct)
    {
        _items.Add(m);
        return Task.CompletedTask;
    }

    public Task<SiteMonitor?> GetAsync(Guid id, string ownerId, CancellationToken ct) =>
        Task.FromResult(_items.FirstOrDefault(m => m.Id == id && m.OwnerId == ownerId));

    public Task<IReadOnlyList<SiteMonitor>> ListByOwnerAsync(string ownerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SiteMonitor>>(_items.Where(m => m.OwnerId == ownerId).ToList());

    public Task<int> CountByOwnerAsync(string ownerId, CancellationToken ct) =>
        Task.FromResult(_items.Count(m => m.OwnerId == ownerId));

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct) =>
        Task.FromResult(_items.RemoveAll(m => m.Id == id && m.OwnerId == ownerId) > 0);

    public Task UpdateAsync(SiteMonitor m, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<SiteMonitor>> ClaimDueAsync(DateTime now, int limit, TimeSpan lease, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SiteMonitor>>(
            _items.Where(m => m.Enabled && m.NextCheckAt <= now).Take(limit).ToList());

    public Task<bool> SaveCheckAsync(SiteMonitor monitor, CheckResult result, Incident? closedIncident, CancellationToken ct) =>
        Task.FromResult(_items.Contains(monitor));
}
