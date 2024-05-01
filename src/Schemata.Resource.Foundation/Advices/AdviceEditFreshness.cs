using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceEditFreshness<TEntity, TRequest>(IServiceProvider services) : IResourceEditAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    private readonly IServiceProvider _services = services;

    #region IResourceUpdateAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TRequest          request,
        TEntity           entity,
        HttpContext       context,
        CancellationToken ct = default) {
        if (ctx.Has<SuppressEditFreshness>()) {
            return Task.FromResult(true);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(true);
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return Task.FromResult(true);
        }

        var freshness = context.Request.Headers.IfMatch.ToString();

        if (!freshness.StartsWith("W/")) {
            return Task.FromResult(true);
        }

        if (freshness != $"W/\"{concurrency.Timestamp}\"") {
            throw new ConcurrencyException();
        }

        return Task.FromResult(true);
    }

    #endregion
}
