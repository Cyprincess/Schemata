using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Grpc.Integration.Tests.Fixtures;

public sealed class AdviceAddSubscriptionName : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription entity, CancellationToken ct = default) {
        entity.Name ??= Guid.NewGuid().ToString("n");
        return Task.FromResult(AdviseResult.Continue);
    }
}
