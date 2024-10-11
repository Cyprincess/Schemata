using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceEditFreshness<TEntity, TRequest> : IResourceEditAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceEditAdvice<TEntity,TRequest> Members

    public int Order => 300_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TRequest          request,
        TEntity           entity,
        HttpContext       http,
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

        var tag = request switch {
            IFreshness freshness => freshness.EntityTag,
            var _ when http.Request.Query.ContainsKey(SchemataConstants.Parameters.EntityTag) => http.Request.Query[SchemataConstants.Parameters.EntityTag].ToString(),
            var _ => http.Request.Headers.IfMatch.ToString(),
        };

        if (string.IsNullOrWhiteSpace(tag)) {
            return Task.FromResult(true);
        }

        if (!tag.StartsWith("W/")) {
            return Task.FromResult(true);
        }

        if (tag != $"W/\"{concurrency.Timestamp.Value.ToByteArray().ToBase64UrlString()}\"") {
            throw new ConcurrencyException();
        }

        return Task.FromResult(true);
    }

    #endregion
}
