using Microsoft.EntityFrameworkCore;
using Schemata.Actor.Skeleton.Entities;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Minimal EF Core context carrying only <see cref="SchemataActor" />, for the persistence integration test.</summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<SchemataActor> Actors { get; set; } = null!;
}
