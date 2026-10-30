using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

public sealed class UserProfileStoreTests(PostgresFixture postgres) : PersistenceTestBase(postgres)
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

    /// <summary>Each call runs in its own DI scope (new DbContext), like separate requests.</summary>
    private static async Task<T> InScope<T>(ServiceProvider provider, Func<IUserProfileStore, Task<T>> action)
    {
        await using var scope = provider.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IUserProfileStore>());
    }

    private static async Task<string> CreateUserAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await users.CreateAsync(user, "Test-Passw0rd!")).Succeeded);
        return user.Id;
    }

    [Fact]
    public async Task Chat_id_round_trip_set_get_clear()
    {
        await using var provider = BuildProvider();
        var userId = await CreateUserAsync(provider);

        Assert.Null(await InScope(provider, s => s.GetTelegramChatIdAsync(userId, Ct)));

        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, "-1001234567890", Ct); return 0; });
        Assert.Equal("-1001234567890", await InScope(provider, s => s.GetTelegramChatIdAsync(userId, Ct)));

        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, "   ", Ct); return 0; });
        Assert.Null(await InScope(provider, s => s.GetTelegramChatIdAsync(userId, Ct)));

        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, "42", Ct); return 0; });
        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, null, Ct); return 0; });
        Assert.Null(await InScope(provider, s => s.GetTelegramChatIdAsync(userId, Ct)));
    }

    [Fact]
    public async Task Chat_id_is_trimmed_and_isolated_per_user()
    {
        await using var provider = BuildProvider();
        var alice = await CreateUserAsync(provider);
        var bob = await CreateUserAsync(provider);

        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(alice, "  111  ", Ct); return 0; });
        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(bob, "@bob_channel", Ct); return 0; });

        Assert.Equal("111", await InScope(provider, s => s.GetTelegramChatIdAsync(alice, Ct)));
        Assert.Equal("@bob_channel", await InScope(provider, s => s.GetTelegramChatIdAsync(bob, Ct)));

        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(alice, null, Ct); return 0; });
        Assert.Null(await InScope(provider, s => s.GetTelegramChatIdAsync(alice, Ct)));
        Assert.Equal("@bob_channel", await InScope(provider, s => s.GetTelegramChatIdAsync(bob, Ct)));
    }

    [Fact]
    public async Task Invalid_chat_id_is_rejected_and_nothing_is_stored()
    {
        await using var provider = BuildProvider();
        var userId = await CreateUserAsync(provider);
        await InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, "7", Ct); return 0; });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            InScope(provider, async s => { await s.SetTelegramChatIdAsync(userId, "not a chat id", Ct); return 0; }));

        Assert.Equal("7", await InScope(provider, s => s.GetTelegramChatIdAsync(userId, Ct)));
    }

    [Fact]
    public async Task Unknown_user_has_no_chat_id_and_cannot_be_updated()
    {
        await using var provider = BuildProvider();

        Assert.Null(await InScope(provider, s => s.GetTelegramChatIdAsync("missing", Ct)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InScope(provider, async s => { await s.SetTelegramChatIdAsync("missing", "1", Ct); return 0; }));
    }
}
