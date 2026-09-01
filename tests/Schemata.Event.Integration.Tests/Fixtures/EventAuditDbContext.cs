using Microsoft.EntityFrameworkCore;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Integration.Tests.Fixtures;

public class EventAuditDbContext : DbContext
{
    public EventAuditDbContext(DbContextOptions<EventAuditDbContext> options) : base(options) { }

    public DbSet<SchemataEvent> SchemataEvents { get; set; } = null!;
}
