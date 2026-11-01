using Microsoft.EntityFrameworkCore;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class TenantFlowDbContext : DbContext
{
    public TenantFlowDbContext(DbContextOptions<TenantFlowDbContext> options) : base(options) { }

    public DbSet<TenantProcess>              Processes     { get; set; } = null!;
    public DbSet<TenantProcessToken>         Tokens        { get; set; } = null!;
    public DbSet<TenantProcessTransition>    Transitions   { get; set; } = null!;
    public DbSet<TenantProcessCompensation>  Compensations { get; set; } = null!;
    public DbSet<TenantProcessParticipant>   Participants  { get; set; } = null!;
    public DbSet<TenantProcessSource>        Sources       { get; set; } = null!;
    public DbSet<TenantFlowEffectIntent>     EffectIntents { get; set; } = null!;
}
