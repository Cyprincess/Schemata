using Microsoft.EntityFrameworkCore;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Integration.Tests.Fixtures;

public class SchedulingDbContext : DbContext
{
    public SchedulingDbContext(DbContextOptions<SchedulingDbContext> options) : base(options) { }

    public DbSet<SchemataJob>          SchemataJobs          { get; set; } = null!;
    public DbSet<SchemataJobExecution> SchemataJobExecutions { get; set; } = null!;
}
