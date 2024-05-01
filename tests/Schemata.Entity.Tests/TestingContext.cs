using Microsoft.EntityFrameworkCore;
using Schemata.Entity.Tests.Entity;

namespace Schemata.Entity.Tests;

public class TestingContext(DbContextOptions<TestingContext> options) : DbContext(options)
{
    public DbSet<Student> Students { get; set; } = null!;
}
