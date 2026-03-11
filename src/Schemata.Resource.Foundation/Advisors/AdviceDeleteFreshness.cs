using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceDeleteFreshness<TEntity> : IResourceDeleteAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceDeleteAdvisor<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity           entity,
        DeleteRequest     request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (request.Force) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!FreshnessHelper.TryGetEntityTag(ctx, entity, out var expected)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var tag = request.Etag;

        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("W/")) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (tag != expected) {
            throw new ConcurrencyException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
