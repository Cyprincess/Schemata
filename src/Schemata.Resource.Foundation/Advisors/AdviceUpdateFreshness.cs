using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceUpdateFreshness<TEntity, TRequest> : IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    #region IResourceUpdateAdvisor<TEntity,TRequest> Members

    public int Order => 300_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (ctx.Has<SuppressFreshness>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (concurrency.Timestamp == null || concurrency.Timestamp.Value == Guid.Empty) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var freshness = http?.Request.Query[SchemataConstants.Parameters.EntityTag].ToString();

        if (string.IsNullOrWhiteSpace(freshness)) {
            freshness = http?.Request.Headers.IfMatch.ToString();
        }

        if (string.IsNullOrWhiteSpace(freshness)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!freshness.StartsWith("W/")) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (freshness != $"W/\"{concurrency.Timestamp.Value.ToByteArray().ToBase64UrlString()}\"") {
            throw new ConcurrencyException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
