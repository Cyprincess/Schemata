using Microsoft.EntityFrameworkCore;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Buyer> Buyers { get; set; } = null!;
}
