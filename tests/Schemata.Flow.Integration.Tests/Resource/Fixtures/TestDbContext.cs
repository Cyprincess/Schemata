using Microsoft.EntityFrameworkCore;

namespace Schemata.Flow.Integration.Tests.Resource.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;

    public DbSet<Trash> Trashes { get; set; } = null!;

    public DbSet<Schemata.Scheduling.Skeleton.Entities.SchemataJob> Jobs { get; set; } = null!;

    public DbSet<Schemata.Scheduling.Skeleton.Entities.SchemataJobExecution> Executions { get; set; } = null!;

    public DbSet<Skeleton.Entities.SchemataProcess> Processes { get; set; } = null!;

    public DbSet<Skeleton.Entities.SchemataProcessToken> ProcessTokens { get; set; } = null!;

    public DbSet<Skeleton.Entities.SchemataProcessTransition> ProcessTransitions { get; set; } = null!;

    public DbSet<Skeleton.Entities.SchemataProcessSource> ProcessSources { get; set; } = null!;

    public DbSet<Skeleton.Entities.SchemataProcessCompensation> ProcessCompensations { get; set; } = null!;
}
