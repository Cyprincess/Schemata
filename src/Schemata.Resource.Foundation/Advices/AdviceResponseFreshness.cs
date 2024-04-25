using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advices;

public class AdviceResponseFreshness<TEntity, TDetail> : IResourceResponseAdvice<TEntity, TDetail>
    where TEntity : class, IIdentifier
    where TDetail : class, IIdentifier
{
    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext     ctx,
        TEntity           entity,
        TDetail?          detail,
        HttpContext       context,
        CancellationToken ct = default) {
        if (entity is not IConcurrency concurrency || detail is not IFreshness freshness) {
            return Task.FromResult(true);
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return Task.FromResult(true);
        }

        freshness.EntityTag = $"W/\"{concurrency.Timestamp}\"";

        return Task.FromResult(true);
    }
}
