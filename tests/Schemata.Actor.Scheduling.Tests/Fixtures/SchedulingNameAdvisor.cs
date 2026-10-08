using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Actor.Scheduling.Tests.Fixtures;

internal sealed class SchedulingNameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class, ICanonicalName, IIdentifier
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<TEntity> repository, TEntity entity, CancellationToken ct
    ) {
        entity.Name ??= $"consumer-{entity.Uid:N}";
        return Task.FromResult(AdviseResult.Continue);
    }
}
