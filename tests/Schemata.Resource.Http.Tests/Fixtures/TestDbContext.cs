using Microsoft.EntityFrameworkCore;

namespace Schemata.Resource.Http.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;
}
