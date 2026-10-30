using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

/// <summary>Deleting an account (what DeletePersonalData does) must take its monitors, results and incidents with it.</summary>
public sealed class OwnerDeletionTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Postgres.ConnectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<AppDbContext>();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<ApplicationUser> CreateUserAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, "Test-Passw0rd!")).Succeeded);
        return user;
    }

    [Fact]
    public async Task Deleting_a_user_deletes_his_monitors_results_and_incidents_but_not_other_users_data()
    {
        await using var provider = BuildProvider();
        var leaving = await CreateUserAsync(provider);
        var staying = await CreateUserAsync(provider);
        var gone = await AddMonitorWithOpenIncident(owner: leaving.Id);
        var kept = await AddMonitorWithOpenIncident(owner: staying.Id);

        await using (var scope = provider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync(await users.FindByIdAsync(leaving.Id) ?? throw new InvalidOperationException())).Succeeded);
        }

        await using var db = Postgres.CreateContext();
        Assert.Equal([kept.Id], await db.Monitors.Select(m => m.Id).ToListAsync(Ct));
        var results = await db.CheckResults.Select(r => r.MonitorId).ToListAsync(Ct);
        Assert.NotEmpty(results);
        Assert.All(results, id => Assert.Equal(kept.Id, id));
        var incidents = await db.Incidents.Select(i => i.MonitorId).ToListAsync(Ct);
        Assert.Equal([kept.Id], incidents);
        Assert.DoesNotContain(gone.Id, results);
    }

    [Fact]
    public async Task Claim_due_no_longer_returns_monitors_of_a_deleted_user()
    {
        await using var provider = BuildProvider();
        var leaving = await CreateUserAsync(provider);
        var staying = await CreateUserAsync(provider);
        await WithMonitors(r => r.AddAsync(NewMonitor(leaving.Id, "https://gone.example.com"), Ct));
        var kept = NewMonitor(staying.Id, "https://kept.example.com");
        await WithMonitors(r => r.AddAsync(kept, Ct));

        Assert.Equal(2, (await WithMonitors(r => r.ClaimDueAsync(T.AddMinutes(1), 10, TimeSpan.Zero, Ct))).Count);

        await using (var scope = provider.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync(await users.FindByIdAsync(leaving.Id) ?? throw new InvalidOperationException())).Succeeded);
        }

        var claimed = await WithMonitors(r => r.ClaimDueAsync(T.AddMinutes(1), 10, TimeSpan.Zero, Ct));
        Assert.Equal([kept.Id], claimed.Select(m => m.Id));
    }

    [Fact]
    public async Task Monitor_for_an_unknown_owner_is_rejected_by_the_foreign_key()
    {
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            WithMonitors(r => r.AddAsync(NewMonitor("no-such-user"), Ct)));
    }
}
