using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceDeleteFreshness<TEntity> : IResourceDeleteAdvice<TEntity> where TEntity : class, IIdentifier
{
    #region IResourceDeleteAdvice<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TEntity           entity,
        HttpContext       http,
        CancellationToken ct = default) {
        if (ctx.Has<SuppressDeleteFreshness>()) {
            return Task.FromResult(true);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(true);
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return Task.FromResult(true);
        }

        var freshness = http.Request.Query[SchemataConstants.Parameters.EntityTag].ToString();

        if (string.IsNullOrWhiteSpace(freshness)) {
            freshness = http.Request.Headers.IfMatch.ToString();
        }

        if (string.IsNullOrWhiteSpace(freshness)) {
            return Task.FromResult(true);
        }

        if (!freshness.StartsWith("W/")) {
            return Task.FromResult(true);
        }

        if (freshness != $"W/\"{concurrency.Timestamp.Value.ToByteArray().ToBase64UrlString()}\"") {
            throw new ConcurrencyException();
        }

        return Task.FromResult(true);
    }

    #endregion
}
