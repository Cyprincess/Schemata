using Microsoft.EntityFrameworkCore;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Actor.Tests.Fixtures;

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<SchemataPushSubscription> Subscriptions { get; set; } = null!;
}