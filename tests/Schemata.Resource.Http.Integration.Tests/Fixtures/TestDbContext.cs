using Microsoft.EntityFrameworkCore;

namespace Schemata.Resource.Http.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;

    public DbSet<LockedStudent> LockedStudents { get; set; } = null!;

    public DbSet<Trash> Trashes { get; set; } = null!;

    public DbSet<PagedThing> PagedThings { get; set; } = null!;
    public DbSet<ParentedRecord> ParentedRecords { get; set; } = null!;

    public DbSet<IdempotentOrder> IdempotentOrders { get; set; } = null!;

    public DbSet<HttpNote> HttpNotes { get; set; } = null!;
    public DbSet<GrpcNote> GrpcNotes { get; set; } = null!;

    public DbSet<Scheduling.Skeleton.Entities.SchemataJob> Jobs { get; set; } = null!;

    public DbSet<Scheduling.Skeleton.Entities.SchemataJobExecution> Executions { get; set; } = null!;

    public DbSet<Flow.Skeleton.Entities.SchemataProcess> Processes { get; set; } = null!;

    public DbSet<Flow.Skeleton.Entities.SchemataProcessToken> ProcessTokens { get; set; } = null!;

    public DbSet<Flow.Skeleton.Entities.SchemataProcessTransition> ProcessTransitions { get; set; } = null!;

    public DbSet<Flow.Skeleton.Entities.SchemataProcessSource> ProcessSources { get; set; } = null!;

    public DbSet<Flow.Skeleton.Entities.SchemataProcessCompensation> ProcessCompensations { get; set; } = null!;
}
