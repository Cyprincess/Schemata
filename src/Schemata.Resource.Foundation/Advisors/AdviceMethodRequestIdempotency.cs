using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Caching.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceMethodRequestIdempotency{TEntity, TRequest, TResponse}" />.
/// </summary>
public static class AdviceMethodRequestIdempotency
{
    /// <summary>
    ///     Default order: runs after
    ///     <see cref="AdviceMethodRequestAuthorize{TEntity, TRequest}" /> --
    ///     idempotency is the last link in the custom-method request chain.
    /// </summary>
    public const int DefaultOrder = AdviceMethodRequestAuthorize.DefaultOrder + 10_000_000;
}

/// <summary>
///     Provides AIP-136 custom-method request idempotency
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso> by checking the
///     <see cref="ICacheProvider" /> for a cached result keyed by
///     <see cref="PendingIdempotencyKey" />, using the lowerCamelCase verb stashed in
///     <see cref="ResourceMethodVerb" /> as the operation token.
///     If found with a matching payload hash, returns <see cref="AdviseResult.Handle" /> with the
///     cached <typeparamref name="TResponse" /> placed in the context; a hash mismatch raises
///     <see cref="ConcurrencyException" />.
///     Otherwise, stores a <see cref="PendingIdempotencyKey" /> in the context for
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> to persist the result
///     after a successful method invocation.
///     Suppressed when <see cref="MethodIdempotencySuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class AdviceMethodRequestIdempotency<TEntity, TRequest, TResponse>(ICacheProvider cache, TimeProvider? time = null)
    : AdviceRequestIdempotencyBase<TEntity, TRequest, TResponse>(cache, time), IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName
{
    public int Order => AdviceMethodRequestIdempotency.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        return AdviseCoreAsync(ctx, request, principal, ct);
    }

    protected override string? Operation(AdviceContext ctx) {
        return ctx.TryGet<ResourceMethodVerb>(out var marker) ? marker?.Verb : null;
    }

    protected override bool IsSuppressed(AdviceContext ctx) {
        return ctx.Has<MethodIdempotencySuppressed>();
    }

    protected override void StoreReplay(AdviceContext ctx, TResponse payload) {
        ctx.Set(payload);
    }
}
