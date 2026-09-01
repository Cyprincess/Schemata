using Microsoft.EntityFrameworkCore;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Actor.Tests.Fixtures;

/// <summary>Minimal EF Core context carrying the process entities the concurrency suite persists to.</summary>
public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<SchemataProcess>             Processes     { get; set; } = null!;
    public DbSet<SchemataProcessToken>        Tokens        { get; set; } = null!;
    public DbSet<SchemataProcessTransition>   Transitions   { get; set; } = null!;
    public DbSet<SchemataProcessSource>       Sources       { get; set; } = null!;
    public DbSet<SchemataProcessCompensation> Compensations { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<SchemataProcess>().Ignore(process => process.DisplayNames);
        modelBuilder.Entity<SchemataProcess>().Ignore(process => process.Descriptions);
        modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Annotations);
        modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Bookkeeping);
    }
}
