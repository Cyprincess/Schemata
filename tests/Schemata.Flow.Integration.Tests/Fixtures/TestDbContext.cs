using Microsoft.EntityFrameworkCore;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Order>                     Orders      { get; set; } = null!;
    public DbSet<OwnedOrder>                OwnedOrders { get; set; } = null!;
    public DbSet<SchemataProcess>           Processes   { get; set; } = null!;
    public DbSet<SchemataProcessToken>      Tokens      { get; set; } = null!;
    public DbSet<SchemataProcessTransition> Transitions { get; set; } = null!;
    public DbSet<SchemataProcessSource>     Sources     { get; set; } = null!;
    public DbSet<SchemataProcessCompensation> Compensations { get; set; } = null!;
    public DbSet<SchemataJob>               Jobs        { get; set; } = null!;
    public DbSet<SchemataJobExecution>      Executions  { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        modelBuilder.Entity<SchemataProcess>().Ignore(process => process.DisplayNames);
        modelBuilder.Entity<SchemataProcess>().Ignore(process => process.Descriptions);
        modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Annotations);
        modelBuilder.Entity<SchemataProcessToken>().Ignore(token => token.Bookkeeping);
    }
}
