using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;

namespace Schemata.Flow.Foundation;

/// <summary>Joined repositories and unit of work for a Flow operation.</summary>
public sealed class FlowPersistenceScope(
    IUnitOfWork                              unitOfWork,
    IRepository<SchemataProcess>             processes,
    IRepository<SchemataProcessToken>        tokens,
    IRepository<SchemataProcessTransition>   transitions,
    IRepository<SchemataProcessSource>       sources,
    IRepository<SchemataProcessCompensation> compensations
)
{
    /// <summary>The unit of work shared by all repositories.</summary>
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;

    public IRepository<SchemataProcess> Processes { get; } = processes;

    public IRepository<SchemataProcessToken> Tokens { get; } = tokens;

    public IRepository<SchemataProcessTransition> Transitions { get; } = transitions;

    public IRepository<SchemataProcessSource> Sources { get; } = sources;

    public IRepository<SchemataProcessCompensation> Compensations { get; } = compensations;
}