using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UptimeWatch.Application.Tests.Fakes;
using UptimeWatch.Domain;

namespace UptimeWatch.Application.Tests;

public class MonitorServiceTests
{
    private readonly InMemoryMonitorRepository _repo = new();
    private readonly ICheckResultRepository _results = Substitute.For<ICheckResultRepository>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly MonitorService _sut;

    public MonitorServiceTests() => _sut = new MonitorService(_repo, _results, _time);

    [Fact]
    public async Task Create_persists_monitor_for_owner_and_returns_dto()
    {
        var result = await _sut.CreateAsync("u1", "  https://example.com  ", 60, 200, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.Value);
        Assert.Equal("https://example.com", result.Value.Url);
        Assert.Equal(60, result.Value.IntervalSeconds);
        Assert.Equal(200, result.Value.ExpectedStatusCode);
        Assert.True(result.Value.Enabled);
        Assert.False(result.Value.IsDown);
        var stored = Assert.Single(_repo.Items);
        Assert.Equal("u1", stored.OwnerId);
        Assert.Equal(result.Value.Id, stored.Id);
        Assert.Equal("https://example.com", stored.Url);
    }

    [Fact]
    public async Task Create_returns_error_for_invalid_url_and_does_not_persist()
    {
        var result = await _sut.CreateAsync("u1", "http://localhost", 60, 200, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.Equal(ErrorCode.UrlLocalAddress, result.Error?.Code);
        Assert.Empty(_repo.Items);
    }

    [Fact]
    public async Task Create_returns_error_when_owner_already_has_20_monitors()
    {
        for (var i = 0; i < MonitorLimits.MaxMonitorsPerUser; i++)
        {
            var created = await _sut.CreateAsync("u1", $"https://example.com/{i}", 60, 200, CancellationToken.None);
            Assert.Null(created.Error);
        }

        var result = await _sut.CreateAsync("u1", "https://example.com/extra", 60, 200, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.Equal(ErrorCode.MonitorLimitReached, result.Error?.Code);
        Assert.Equal([MonitorLimits.MaxMonitorsPerUser], result.Error!.Args);
        Assert.Equal(MonitorLimits.MaxMonitorsPerUser, _repo.Items.Count);

        var other = await _sut.CreateAsync("u2", "https://example.com/extra", 60, 200, CancellationToken.None);
        Assert.Null(other.Error);
    }

    [Fact]
    public async Task Get_returns_null_for_other_owners_monitor_and_for_unknown_id()
    {
        var created = (await _sut.CreateAsync("u1", "https://example.com", 60, 200, CancellationToken.None)).Value!;

        Assert.Null(await _sut.GetAsync(created.Id, "u2", CancellationToken.None));
        Assert.Null(await _sut.GetAsync(Guid.NewGuid(), "u1", CancellationToken.None));
        Assert.NotNull(await _sut.GetAsync(created.Id, "u1", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_by_other_owner_returns_false_and_keeps_monitor()
    {
        var created = (await _sut.CreateAsync("u1", "https://example.com", 60, 200, CancellationToken.None)).Value!;

        Assert.False(await _sut.DeleteAsync(created.Id, "u2", CancellationToken.None));
        Assert.Single(_repo.Items);

        Assert.True(await _sut.DeleteAsync(created.Id, "u1", CancellationToken.None));
        Assert.Empty(_repo.Items);
    }

    [Fact]
    public async Task List_returns_only_owners_monitors()
    {
        await _sut.CreateAsync("u1", "https://a.example.com", 60, 200, CancellationToken.None);
        await _sut.CreateAsync("u1", "https://b.example.com", 60, 200, CancellationToken.None);
        await _sut.CreateAsync("u2", "https://c.example.com", 60, 200, CancellationToken.None);

        var list = await _sut.ListAsync("u1", CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.All(list, d => Assert.EndsWith(".example.com", d.Url));
        Assert.DoesNotContain(list, d => d.Url == "https://c.example.com");
    }

    [Fact]
    public async Task SetEnabled_by_other_owner_returns_false()
    {
        var created = (await _sut.CreateAsync("u1", "https://example.com", 60, 200, CancellationToken.None)).Value!;

        Assert.False(await _sut.SetEnabledAsync(created.Id, "u2", false, CancellationToken.None));
        Assert.True(_repo.Items[0].Enabled);

        Assert.True(await _sut.SetEnabledAsync(created.Id, "u1", false, CancellationToken.None));
        Assert.False(_repo.Items[0].Enabled);
    }
}
