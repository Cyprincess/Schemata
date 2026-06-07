namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     update-request idempotency checks
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     When present, <see cref="AdviceUpdateRequestIdempotency{TEntity, TRequest, TDetail}" /> skips
///     the cached-response lookup and treats every request as new.
/// </summary>
public sealed class UpdateIdempotencySuppressed;
