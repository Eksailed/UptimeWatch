using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using UptimeWatch.Infrastructure.Identity;
using UptimeWatch.Infrastructure.Persistence;
using Xunit;

namespace UptimeWatch.Integration.Tests.Support;

/// <summary>One PostgreSQL container per test run; the schema is created by the real EF migrations.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>User ids that repository and cycle tests use as monitor owners; Monitors.OwnerId has a foreign key to the users.</summary>
    public static readonly string[] SeededOwners = ["alice", "bob"];

    /// <summary>Removes all application data (including users) so tests do not see each other's rows, then seeds <see cref="SeededOwners"/>.</summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"CheckResults\", \"Incidents\", \"Monitors\", \"AspNetUsers\" CASCADE");
        context.Users.AddRange(SeededOwners.Select(id => new ApplicationUser
        {
            Id = id,
            UserName = id,
            NormalizedUserName = id.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N")
        }));
        await context.SaveChangesAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
