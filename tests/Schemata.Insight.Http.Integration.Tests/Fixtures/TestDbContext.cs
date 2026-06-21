using Microsoft.EntityFrameworkCore;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;

    public DbSet<Customer> Customers { get; set; } = null!;

    public DbSet<Buyer> Buyers { get; set; } = null!;

    public DbSet<Purchase> Purchases { get; set; } = null!;

    public DbSet<SchemataInsightSource> InsightSources { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        var source = modelBuilder.Entity<SchemataInsightSource>();
        source.HasKey(s => s.Uid);
        source.Ignore(s => s.DisplayNames);
        source.Ignore(s => s.Descriptions);
    }
}
