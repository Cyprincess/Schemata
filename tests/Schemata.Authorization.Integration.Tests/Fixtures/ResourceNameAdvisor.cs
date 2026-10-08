using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Authorization.Integration.Tests.Fixtures;

internal sealed class ResourceNameAdvisor<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => 0;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is ICanonicalName named) {
            named.Name ??= $"resource-{Guid.NewGuid():n}";
        }
        return Task.FromResult(AdviseResult.Continue);
    }
}
