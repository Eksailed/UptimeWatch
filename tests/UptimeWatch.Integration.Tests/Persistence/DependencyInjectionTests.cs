using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

public sealed class DependencyInjectionTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    [Fact]
    public async Task AddInfrastructure_registers_repositories_using_the_Default_connection_string()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Postgres.ConnectionString })
            .Build();
        await using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider(validateScopes: true);

        var monitor = NewMonitor();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMonitorRepository>().AddAsync(monitor, CancellationToken.None);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IMonitorRepository>()
                .GetAsync(monitor.Id, "alice", CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Null(await scope.ServiceProvider.GetRequiredService<ICheckResultRepository>()
                .GetLastAsync(monitor.Id, CancellationToken.None));
        }

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddInfrastructure_fails_clearly_without_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IMonitorRepository>());
        Assert.Contains("ConnectionStrings:Default", ex.Message);
    }
}
