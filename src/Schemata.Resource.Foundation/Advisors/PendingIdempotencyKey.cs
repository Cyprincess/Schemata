namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Tracks a pending idempotency key during a create operation so that
///     <see cref="AdviceResponseIdempotency{TEntity,TDetail}" /> can persist the
///     result after the operation succeeds
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
/// </summary>
/// <param name="RequestId">The client-supplied request identifier.</param>
internal sealed record PendingIdempotencyKey(string RequestId);
