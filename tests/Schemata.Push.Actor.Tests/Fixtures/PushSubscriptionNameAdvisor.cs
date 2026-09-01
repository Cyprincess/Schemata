using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Actor.Tests.Fixtures;

/// <summary>
///     Stamps a stable <see cref="ICanonicalName.Name" /> onto a subscription whose Name is still
///     empty, standing in for the application-supplied Name advisor that spec §5.3 makes the
///     creator's responsibility. Runs before <see cref="AdviceAddCanonicalName{TEntity}" /> so the
///     canonical-name leaf placeholder can bind.
/// </summary>
internal sealed class PushSubscriptionNameAdvisor : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription          entity,
        CancellationToken                 ct
    ) {
        if (string.IsNullOrEmpty(entity.Name)) {
            entity.Name = Guid.NewGuid().ToString("n");
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}