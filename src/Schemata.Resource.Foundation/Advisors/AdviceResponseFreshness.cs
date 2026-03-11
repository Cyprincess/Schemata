using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

public class AdviceResponseFreshness<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, IIdentifier
    where TDetail : class, IIdentifier
{
    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (entity is not IConcurrency concurrency || detail is not IFreshness freshness) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return Task.FromResult(AdviseResult.Continue);
        }

        freshness.EntityTag = $"W/\"{concurrency.Timestamp.Value.ToByteArray().ToBase64UrlString()}\"";

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
