using Microsoft.EntityFrameworkCore;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;

    public DbSet<Trash> Trashes { get; set; } = null!;

    public DbSet<Schemata.Scheduling.Skeleton.Entities.SchemataJob> Jobs { get; set; } = null!;

    public DbSet<Schemata.Scheduling.Skeleton.Entities.SchemataJobExecution> Executions { get; set; } = null!;

    public DbSet<Schemata.Flow.Skeleton.Entities.SchemataProcess> Processes { get; set; } = null!;

    public DbSet<Schemata.Flow.Skeleton.Entities.SchemataProcessToken> ProcessTokens { get; set; } = null!;

    public DbSet<Schemata.Flow.Skeleton.Entities.SchemataProcessTransition> ProcessTransitions { get; set; } = null!;

    public DbSet<Schemata.Flow.Skeleton.Entities.SchemataProcessSource> ProcessSources { get; set; } = null!;

    public DbSet<Schemata.Flow.Skeleton.Entities.SchemataProcessCompensation> ProcessCompensations { get; set; } = null!;
}
