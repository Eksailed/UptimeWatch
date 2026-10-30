using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using UptimeWatch.Application;
using UptimeWatch.Domain;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Checks;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests;

/// <summary>
/// The whole check cycle without the web layer: a monitor created through MonitorService is claimed and checked by
/// CheckRunner over real HTTP against a local site, persisted in real PostgreSQL and notified through a substitute.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FullCycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Owner = "alice";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly DateTime Start = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Start));
    private readonly INotifier _notifier = Substitute.For<INotifier>();
    private TestSite _site = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _site = await TestSite.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = postgres.ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // Registered before AddInfrastructure, which only adds its defaults when nothing is registered yet.
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(new HealthCheckerOptions { AllowPrivateAddresses = true });
        services.AddSingleton<IDnsResolver>(new FakeDnsResolver());
        services.AddInfrastructure(configuration);

        services.AddSingleton(_notifier);
        services.AddSingleton(Substitute.For<IMonitorNotifications>());
        services.AddScoped<CheckProcessor>();
        services.AddScoped<MonitorService>();
        services.AddSingleton<CheckRunner>();
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task Full_cycle_three_failures_open_incident_and_recovery_closes_it()
    {
        _site.ControlledStatus = 500;
        var monitor = await CreateMonitorAsync("/controlled", intervalSeconds: 30);

        // Failures 1 and 2: below the threshold, no alert and no incident yet.
        await RunDueAsync();
        await RunDueAsync(after: Interval);
        Assert.Equal(2, _site.RequestCount);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);
        Assert.Empty(await GetIncidentsAsync(monitor.Id));

        // Failure 3: the incident opens and exactly one alert is sent.
        await RunDueAsync(after: Interval);
        await _notifier.Received(1).NotifyDownAsync(
            Arg.Is<SiteMonitor>(m => m.Id == monitor.Id), Arg.Is<Incident>(i => i.EndedAt == null), Arg.Any<CancellationToken>());
        var incident = Assert.Single(await GetIncidentsAsync(monitor.Id));
        Assert.Null(incident.EndedAt);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, incident.StartedAt);
        var openedAt = incident.StartedAt;

        // More failures while the incident is open: no repeated alerts, still one incident.
        await RunDueAsync(after: Interval);
        await RunDueAsync(after: Interval);
        Assert.Equal(5, _site.RequestCount);
        await _notifier.ReceivedWithAnyArgs(1).NotifyDownAsync(default!, default!, default);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyRecoveredAsync(default!, default!, default);
        Assert.Single(await GetIncidentsAsync(monitor.Id));

        // Recovery: the incident is closed with a positive duration and one recovery alert is sent.
        _site.ControlledStatus = 200;
        Incident? recovered = null;
        await _notifier.NotifyRecoveredAsync(Arg.Any<SiteMonitor>(), Arg.Do<Incident>(i => recovered = i), Arg.Any<CancellationToken>());
        await RunDueAsync(after: Interval);

        await _notifier.Received(1).NotifyRecoveredAsync(
            Arg.Is<SiteMonitor>(m => m.Id == monitor.Id), Arg.Any<Incident>(), Arg.Any<CancellationToken>());
        Assert.NotNull(recovered);
        Assert.True(recovered.Duration > TimeSpan.Zero);

        var stored = Assert.Single(await GetIncidentsAsync(monitor.Id));
        Assert.Equal(openedAt, stored.StartedAt);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, stored.EndedAt);
        Assert.True(stored.Duration > TimeSpan.Zero);

        // Healthy checks afterwards stay silent.
        await RunDueAsync(after: Interval);
        await _notifier.ReceivedWithAnyArgs(1).NotifyDownAsync(default!, default!, default);
        await _notifier.ReceivedWithAnyArgs(1).NotifyRecoveredAsync(default!, default!, default);

        await using var scope = _provider.CreateAsyncScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IMonitorRepository>()
            .GetAsync(monitor.Id, Owner, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.ConsecutiveFailures);
        Assert.Null(loaded.OpenIncident);
    }

    [Fact]
    public async Task RunDue_checks_only_due_monitors()
    {
        var monitor = await CreateMonitorAsync("/ok", intervalSeconds: 60);

        await RunDueAsync();
        Assert.Equal(1, _site.RequestCount);

        await RunDueAsync(after: TimeSpan.FromSeconds(10));
        Assert.Equal(1, _site.RequestCount);

        await RunDueAsync(after: TimeSpan.FromSeconds(50));
        Assert.Equal(2, _site.RequestCount);

        await using var scope = _provider.CreateAsyncScope();
        var recent = await scope.ServiceProvider.GetRequiredService<ICheckResultRepository>()
            .GetRecentAsync(monitor.Id, 10, CancellationToken.None);
        Assert.Equal(2, recent.Count);
        Assert.All(recent, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task Two_overlapping_RunDue_calls_check_each_monitor_once()
    {
        const int monitorCount = 6;
        for (var i = 0; i < monitorCount; i++)
        {
            await CreateMonitorAsync($"/ok?m={i}", intervalSeconds: 60);
        }

        var runner = _provider.GetRequiredService<CheckRunner>();
        await Task.WhenAll(runner.RunDueAsync(CancellationToken.None), runner.RunDueAsync(CancellationToken.None));

        Assert.Equal(monitorCount, _site.RequestCount);
    }

    [Fact]
    public async Task Disabled_monitor_is_never_checked()
    {
        var monitor = await CreateMonitorAsync("/ok", intervalSeconds: 30);
        await SetEnabledAsync(monitor.Id, false);

        await RunDueAsync();
        await RunDueAsync(after: TimeSpan.FromHours(1));

        Assert.Equal(0, _site.RequestCount);
        await _notifier.DidNotReceiveWithAnyArgs().NotifyDownAsync(default!, default!, default);

        // Control: enabling it makes it due again, so the zero above is not an artefact of the setup.
        await SetEnabledAsync(monitor.Id, true);
        await RunDueAsync();
        Assert.Equal(1, _site.RequestCount);
    }

    private async Task<MonitorStatusDto> CreateMonitorAsync(string path, int intervalSeconds)
    {
        await using var scope = _provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<MonitorService>()
            .CreateAsync(Owner, _site.Url(path), intervalSeconds, 200, CancellationToken.None);
        Assert.Null(result.Error);
        return result.Value!;
    }

    private async Task SetEnabledAsync(Guid id, bool enabled)
    {
        await using var scope = _provider.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<MonitorService>()
            .SetEnabledAsync(id, Owner, enabled, CancellationToken.None));
    }

    /// <summary>Advances the fake clock by <paramref name="after"/> (if given) and runs one scheduler tick.</summary>
    private async Task RunDueAsync(TimeSpan? after = null)
    {
        if (after is { } delta)
        {
            _time.Advance(delta);
        }

        await _provider.GetRequiredService<CheckRunner>().RunDueAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<Incident>> GetIncidentsAsync(Guid monitorId)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICheckResultRepository>()
            .GetIncidentsAsync(monitorId, 10, CancellationToken.None);
    }
}
