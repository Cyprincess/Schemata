using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Event.Skeleton.Entities;

namespace Schemata.Event.Integration.Tests.Fixtures;

internal sealed class EventAuditNameAdvisor : IRepositoryAddAdvisor<SchemataEvent>
{
    public int Order => 50_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                  ctx,
        IRepository<SchemataEvent>     repository,
        SchemataEvent                  entity,
        CancellationToken              ct
    ) {
        entity.Name = entity.EventType;

        return Task.FromResult(AdviseResult.Continue);
    }
}
