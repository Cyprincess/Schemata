using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

internal sealed class SessionActorNameAdvisor : IRepositoryAddAdvisor<SessionActor>
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<SessionActor> repository, SessionActor entity, MutationOptions options, CancellationToken ct
    ) {
        entity.Name ??= $"session-{entity.Uid:N}";
        return Task.FromResult(AdviseResult.Continue);
    }
}
