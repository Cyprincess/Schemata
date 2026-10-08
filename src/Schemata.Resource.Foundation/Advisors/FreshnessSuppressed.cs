namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     freshness (ETag) checks and generation for the current dispatch
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
public sealed class FreshnessSuppressed;
