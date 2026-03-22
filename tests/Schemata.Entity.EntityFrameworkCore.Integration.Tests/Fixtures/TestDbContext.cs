using Microsoft.EntityFrameworkCore;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;
}
