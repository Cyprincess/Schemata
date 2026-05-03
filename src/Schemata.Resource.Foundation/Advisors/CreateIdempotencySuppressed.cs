namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     create-request idempotency checks
///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
///     When present, <see cref="AdviceCreateRequestIdempotency{TEntity,TRequest,TDetail}" /> skips
///     the cached-response lookup and treats every request as new.
/// </summary>
public sealed class CreateIdempotencySuppressed;
