namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
/// Marker type that, when set in the advice context, suppresses create-request idempotency checks.
/// </summary>
/// <remarks>
/// When present in the <see cref="Schemata.Abstractions.Advisors.AdviceContext"/>,
/// <see cref="AdviceCreateRequestIdempotency{TEntity, TRequest, TDetail}"/> skips the cached-response lookup.
/// </remarks>
internal sealed class SuppressCreateIdempotency;
