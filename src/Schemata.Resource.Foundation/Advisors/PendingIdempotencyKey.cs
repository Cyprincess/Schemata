using System;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Tracks a pending idempotency key during a resource operation so that
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> can persist the
///     result after the operation succeeds
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     The cache key partitions on entity type, operation, and caller so distinct
///     resources, verbs, or principals sharing the same client-supplied
///     <paramref name="RequestId" /> never observe each other's results. The
///     <paramref name="PayloadHash" /> travels with the cached value (see
///     <see cref="IdempotencyEnvelope{TResult}" />) so a replay with a different
///     payload is rejected.
/// </summary>
/// <param name="RequestId">The client-supplied request identifier.</param>
/// <param name="Operation">The operation token --
///     <c>nameof(Operations.Create)</c> / <c>nameof(Operations.Update)</c> for
///     CRUD, the lowerCamelCase verb (e.g. <c>"run"</c>, <c>"archive"</c>) for
///     AIP-136 custom methods, <c>nameof(BatchVerb.BatchCreate)</c> etc. for
///     AIP-231..235 batch methods.</param>
/// <param name="EntityType">The CLR full name of the resource entity type.</param>
/// <param name="Principal">The caller identifier from <see cref="IdempotencyHelper.PrincipalId" />.</param>
/// <param name="PayloadHash">The request payload hash from <see cref="IdempotencyHelper.HashPayload{TRequest}" />.</param>
internal sealed record PendingIdempotencyKey(
    string RequestId,
    string Operation,
    string EntityType,
    string Principal,
    string PayloadHash
)
{
    /// <summary>
    ///     Builds the cache key shared by the reservation advisors, the response advisors,
    ///     and the failure-release path.
    /// </summary>
    public string ToCacheKey() {
        return $"idempotency\x1e{EntityType}\x1e{Operation}\x1e{Principal}\x1e{RequestId}".ToCacheKey(Keys.Resource);
    }
}
