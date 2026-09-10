using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Resource.Grpc.Integration.Tests.Fixtures;

internal sealed class AdviceAddResourceName<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx, IRepository<TEntity> repository, TEntity entity, CancellationToken ct
    ) {
        if (string.IsNullOrWhiteSpace(entity.Name)) {
            entity.Name = $"consumer-{Guid.NewGuid():n}";
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
