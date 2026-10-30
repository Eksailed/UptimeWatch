using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UptimeWatch.Domain;
using UptimeWatch.Infrastructure.Identity;

namespace UptimeWatch.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SiteMonitor> Monitors => Set<SiteMonitor>();

    public DbSet<CheckResult> CheckResults => Set<CheckResult>();

    public DbSet<Incident> Incidents => Set<Incident>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>().HaveColumnType("timestamp with time zone");
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>().HaveColumnType("timestamp with time zone");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(b => b.Property(u => u.TelegramChatId).HasMaxLength(64));

        modelBuilder.Entity<SiteMonitor>(b =>
        {
            b.ToTable("Monitors");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.OwnerId).IsRequired().HasMaxLength(450);
            b.Property(m => m.Url).IsRequired().HasMaxLength(MonitorLimits.MaxUrlLength);

            // The open incident is a row of Incidents with EndedAt IS NULL; the repository sets it after loading.
            b.Ignore(m => m.OpenIncident);

            b.HasIndex(m => m.OwnerId);
            b.HasIndex(m => m.NextCheckAt);
        });

        modelBuilder.Entity<CheckResult>(b =>
        {
            b.ToTable("CheckResults");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();

            b.HasOne<SiteMonitor>().WithMany().HasForeignKey(r => r.MonitorId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(r => new { r.MonitorId, r.CheckedAt });
            b.HasIndex(r => r.CheckedAt);
        });

        modelBuilder.Entity<Incident>(b =>
        {
            b.ToTable("Incidents");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).ValueGeneratedNever();

            b.HasOne<SiteMonitor>().WithMany().HasForeignKey(i => i.MonitorId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(i => new { i.MonitorId, i.StartedAt });

            // At most one open incident per monitor.
            b.HasIndex(i => i.MonitorId).IsUnique().HasFilter("\"EndedAt\" IS NULL").HasDatabaseName("IX_Incidents_MonitorId_Open");
        });
    }
}
