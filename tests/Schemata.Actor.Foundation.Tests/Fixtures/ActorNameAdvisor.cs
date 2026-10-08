using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

internal sealed class ActorNameAdvisor : IRepositoryAddAdvisor<SchemataActor>
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<SchemataActor> repository, SchemataActor entity, CancellationToken ct
    ) {
        entity.Name ??= $"consumer-{entity.Uid:N}";
        return Task.FromResult(AdviseResult.Continue);
    }
}
