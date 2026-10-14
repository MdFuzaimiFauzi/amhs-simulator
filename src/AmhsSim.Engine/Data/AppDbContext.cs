using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AmhsSim.Engine.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TransferJob> Jobs => Set<TransferJob>();
    public DbSet<JobEvent> JobEvents => Set<JobEvent>();
    public DbSet<AlarmRecord> Alarms => Set<AlarmRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Store enums as readable text, not numbers, so the DB makes sense in a viewer.
        builder.Properties<JobState>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<JobTrigger>().HaveConversion<string>().HaveMaxLength(32);

        // Give every string a length. Without one, SQL Server uses nvarchar(max), which can't be indexed.
        builder.Properties<string>().HaveMaxLength(200);

        // Every DateTime goes in as UTC and comes back marked as UTC, whichever database is behind it.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TransferJob>().HasIndex(j => j.State);
        model.Entity<TransferJob>().Property(j => j.Note).HasMaxLength(1000);
        model.Entity<JobEvent>().Property(e => e.Note).HasMaxLength(1000);
        model.Entity<AlarmRecord>().Property(a => a.Message).HasMaxLength(1000);
        model.Entity<AlarmRecord>().HasIndex(a => new { a.Source, a.ClearedAt });
    }
}

/// <summary>
/// SQLite, SQL Server and Oracle hand DateTimes back with Kind = Unspecified, and PostgreSQL refuses
/// to save one that isn't UTC. This converter makes all four behave the same.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));