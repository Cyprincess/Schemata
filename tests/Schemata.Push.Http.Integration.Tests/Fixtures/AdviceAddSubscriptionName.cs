using System;
using Schemata.Entity.Repository;
using System.Threading.Tasks;
using System.Threading;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Http.Integration.Tests.Fixtures;

public sealed class AdviceAddSubscriptionName : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription entity, CancellationToken ct = default) {
        entity.Name ??= Guid.NewGuid().ToString("n");
        return Task.FromResult(AdviseResult.Continue);
    }
}
