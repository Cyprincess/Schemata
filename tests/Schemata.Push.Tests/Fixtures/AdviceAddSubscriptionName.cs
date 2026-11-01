using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Tests.Fixtures;

/// <summary>
///     Consumer-supplied Name advisor: stamps the next name produced by
///     <paramref name="nextName" /> onto a subscription that carries none. Ordered ahead of
///     <see cref="AdviceAddCanonicalName{TEntity}" /> so the pattern's leaf placeholder binds.
/// </summary>
public sealed class AdviceAddSubscriptionName(Func<string> nextName) : IRepositoryAddAdvisor<SchemataPushSubscription>
{
    public int Order => AdviceAddCanonicalName.DefaultOrder - 1_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                        ctx,
        IRepository<SchemataPushSubscription> repository,
        SchemataPushSubscription             entity,
        MutationOptions      options,
        CancellationToken                    ct
    ) {
        entity.Name ??= nextName();
        return Task.FromResult(AdviseResult.Continue);
    }
}
