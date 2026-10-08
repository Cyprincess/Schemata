using Microsoft.EntityFrameworkCore;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Tests.Fixtures;

public class PushDbContext : DbContext
{
    public PushDbContext(DbContextOptions<PushDbContext> options) : base(options) { }

    public DbSet<SchemataPushSubscription> SchemataPushSubscriptions { get; set; } = null!;
}
