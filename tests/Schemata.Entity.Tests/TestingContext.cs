using Microsoft.EntityFrameworkCore;

namespace Schemata.Entity.Tests;

public class TestingContext : DbContext
{
    public TestingContext(DbContextOptions<TestingContext> options) : base(options) { }

    public DbSet<Student> Students { get; set; } = null!;
}
