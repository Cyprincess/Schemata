using Microsoft.EntityFrameworkCore;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Actor.Scheduling.Tests.Fixtures;

/// <summary>Minimal EF Core context carrying the scheduling entities the reminder pipeline persists to.</summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<SchemataJob> Jobs { get; set; } = null!;

    public DbSet<SchemataJobExecution> Executions { get; set; } = null!;
}
