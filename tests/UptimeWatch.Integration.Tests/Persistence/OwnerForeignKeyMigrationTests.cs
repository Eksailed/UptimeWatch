using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UptimeWatch.Infrastructure.Persistence;
using UptimeWatch.Integration.Tests.Support;

namespace UptimeWatch.Integration.Tests.Persistence;

/// <summary>Upgrading a database that already holds orphan monitors (owner account deleted before the foreign key existed).</summary>
[Collection(PostgresCollection.Name)]
public sealed class OwnerForeignKeyMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migration_removes_orphan_monitors_with_their_data_truncates_long_errors_and_keeps_the_rest()
    {
        var database = "migration_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("Initial");

        var kept = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        var longError = new string('e', 1500);
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "AspNetUsers" ("Id","AccessFailedCount","EmailConfirmed","LockoutEnabled","PhoneNumberConfirmed","TwoFactorEnabled") VALUES ('alive',0,false,false,false,false)""");
        foreach (var (id, owner) in new[] { (kept, "alive"), (orphan, "deleted-user") })
        {
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Monitors" ("Id","OwnerId","Url","IntervalSeconds","ExpectedStatusCode","Enabled","ConsecutiveFailures","NextCheckAt") VALUES ({0},{1},'https://example.com',60,200,true,0,now())""",
                id, owner);
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "CheckResults" ("Id","MonitorId","CheckedAt","Success","ResponseTimeMs","Error") VALUES ({0},{1},now(),false,1,{2})""",
                Guid.NewGuid(), id, longError);
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Incidents" ("Id","MonitorId","StartedAt","LastError") VALUES ({0},{1},now(),{2})""",
                Guid.NewGuid(), id, longError);
        }

        await migrator.MigrateAsync();

        Assert.Equal([kept], await db.Monitors.Select(m => m.Id).ToListAsync());
        var result = await db.CheckResults.SingleAsync();
        Assert.Equal(kept, result.MonitorId);
        Assert.Equal(1000, result.Error!.Length);
        Assert.EndsWith("…", result.Error);
        var incident = await db.Incidents.SingleAsync();
        Assert.Equal(kept, incident.MonitorId);
        Assert.Equal(1000, incident.LastError!.Length);
    }
}
