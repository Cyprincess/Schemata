using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Tracks a pending idempotency key to be stored after the create operation completes.
/// </summary>
/// <param name="RequestId">The client-supplied request identifier.</param>
internal sealed record PendingIdempotencyKey(string RequestId);

public static class AdviceCreateRequestIdempotency
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
/// Provides create-request idempotency by caching results keyed by the client-supplied request ID.
/// </summary>
/// <typeparam name="TEntity">The entity type being created.</typeparam>
/// <typeparam name="TRequest">The request DTO type carrying creation data.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned for cached responses.</typeparam>
/// <remarks>
/// Order: 50,000,000. Auto-registered per resource by <see cref="Features.SchemataResourceFeature.RegisterResource"/>.
/// When the request implements <see cref="Schemata.Abstractions.Resource.IRequestIdentification"/> and provides a <see cref="Schemata.Abstractions.Resource.IRequestIdentification.RequestId"/>,
/// this advisor checks the <see cref="Schemata.Abstractions.Resource.IIdempotencyStore"/> for a cached result.
/// If found, returns <see cref="Schemata.Abstractions.Advisors.AdviseResult.Handle"/> with the cached result.
/// Otherwise, stores a <see cref="PendingIdempotencyKey"/> in the context for
/// <see cref="AdviceResponseIdempotency{TEntity, TDetail}"/> to persist the result after creation.
/// Suppressed when <see cref="SuppressCreateIdempotency"/> is present in the advice context.
/// </remarks>
public sealed class AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly IIdempotencyStore _store;

    public AdviceCreateRequestIdempotency(IIdempotencyStore store) { _store = store; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestIdempotency.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (request is not IRequestIdentification { RequestId: { } requestId }) {
            return AdviseResult.Continue;
        }

        if (ctx.Has<SuppressCreateIdempotency>()) {
            return AdviseResult.Continue;
        }

        var cached = await _store.GetAsync<CreateResult<TDetail>>(requestId, ct);
        if (cached is not null) {
            ctx.Set(cached);
            return AdviseResult.Handle;
        }

        ctx.Set(new PendingIdempotencyKey(requestId));
        return AdviseResult.Continue;
    }

    #endregion
}
