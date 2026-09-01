using Microsoft.EntityFrameworkCore;
using Schemata.Event.Skeleton.Entities;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Push.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Entity.Repository.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<SchemataProcess>           SchemataProcesses           { get; set; } = null!;
    public DbSet<SchemataProcessToken>      SchemataProcessTokens       { get; set; } = null!;
    public DbSet<SchemataProcessTransition> SchemataProcessTransitions  { get; set; } = null!;
    public DbSet<SchemataJob>               SchemataJobs                { get; set; } = null!;
    public DbSet<SchemataJobExecution>      SchemataJobExecutions       { get; set; } = null!;
    public DbSet<SchemataPushSubscription>  SchemataPushSubscriptions   { get; set; } = null!;
    public DbSet<SchemataEvent>             SchemataEvents              { get; set; } = null!;
}