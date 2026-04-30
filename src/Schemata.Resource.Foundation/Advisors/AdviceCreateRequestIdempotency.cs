using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Caching.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceCreateRequestIdempotency{TEntity,TRequest,TDetail}" />.
/// </summary>
public static class AdviceCreateRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceCreateRequestSanitize{TEntity,TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceCreateRequestSanitize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides create-request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by the
///     client-supplied <c>RequestId</c>.
///     If found, returns <see cref="AdviseResult.Handle" /> with the cached result.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity,TDetail}" /> to persist the result
///     after a successful create.
///     Suppressed when <see cref="CreateIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceCreateRequestIdempotency<TEntity, TRequest, TDetail> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    private readonly ICacheProvider _cache;

    /// <summary>
    ///     Initializes a new instance with the cache provider.
    /// </summary>
    /// <param name="cache">The <see cref="ICacheProvider" />.</param>
    public AdviceCreateRequestIdempotency(ICacheProvider cache) { _cache = cache; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestIdempotency.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (request is not IRequestIdentification { RequestId: { } requestId }) {
            return AdviseResult.Continue;
        }

        if (ctx.Has<CreateIdempotencySuppressed>()) {
            return AdviseResult.Continue;
        }

        var key   = $"idempotency\x1e{requestId}".ToCacheKey(Keys.Resource);
        var bytes = await _cache.GetAsync(key, ct);
        if (bytes is null) {
            return default;
        }

        var cached = JsonSerializer.Deserialize<CreateResult<TDetail>>(bytes);
        if (cached is not null) {
            ctx.Set(cached);
            return AdviseResult.Handle;
        }

        ctx.Set(new PendingIdempotencyKey(requestId));
        return AdviseResult.Continue;
    }

    #endregion
}
