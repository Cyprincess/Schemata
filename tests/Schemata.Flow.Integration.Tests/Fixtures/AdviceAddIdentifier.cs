using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Flow.Integration.Tests.Fixtures;

internal sealed class AdviceAddIdentifier<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => AdviceAddConcurrency.DefaultOrder - 10_000_000;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is IIdentifier { Uid: var uid } identified && uid == Guid.Empty) {
            identified.Uid = Identifiers.NewUid();
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
