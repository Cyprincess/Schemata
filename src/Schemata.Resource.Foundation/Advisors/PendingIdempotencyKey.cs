namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Tracks a pending idempotency key during a resource operation so that
///     <see cref="AdviceResponseIdempotency{TEntity, TDetail}" /> can persist the
///     result after the operation succeeds
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     The <paramref name="Operation" /> partitions the cache so distinct
///     operations sharing the same client-supplied <paramref name="RequestId" />
///     do not collide. Final cache key:
///     <c>idempotency\x1e{Operation}\x1e{RequestId}</c>.
/// </summary>
/// <param name="RequestId">The client-supplied request identifier.</param>
/// <param name="Operation">The operation token ──
///     <c>nameof(Operations.Create)</c> / <c>nameof(Operations.Update)</c> for
///     CRUD, the lowerCamelCase verb (e.g. <c>"run"</c>, <c>"archive"</c>) for
///     AIP-136 custom methods.</param>
internal sealed record PendingIdempotencyKey(string RequestId, string Operation);
