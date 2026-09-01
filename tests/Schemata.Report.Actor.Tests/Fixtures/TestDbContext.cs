using Microsoft.EntityFrameworkCore;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Report.Actor.Tests.Fixtures;

/// <summary>EF Core context carrying the source, report, snapshot, and job rows the concurrency suite persists to.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<SourceRecord> SourceRecords { get; set; } = null!;

    public DbSet<SchemataReport> Reports { get; set; } = null!;

    public DbSet<SchemataReportSnapshot> ReportSnapshots { get; set; } = null!;

    public DbSet<SchemataReportSnapshotChunk> ReportSnapshotChunks { get; set; } = null!;

    public DbSet<SchemataJob> Jobs { get; set; } = null!;

    public DbSet<SchemataJobExecution> JobExecutions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder) {
        builder.Entity<SchemataReport>(entity => {
            entity.HasKey(report => report.Uid);
            entity.OwnsOne(report => report.Retention);
        });
        builder.Entity<SchemataReportSnapshot>().HasKey(snapshot => snapshot.Uid);
        builder.Entity<SchemataReportSnapshotChunk>().HasKey(chunk => chunk.Uid);
        builder.Entity<SchemataJob>().HasKey(job => job.Uid);
        builder.Entity<SchemataJobExecution>().HasKey(execution => execution.Uid);
    }
}