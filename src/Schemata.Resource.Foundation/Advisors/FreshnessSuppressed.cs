namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Marker type in <see cref="Schemata.Abstractions.Advisors.AdviceContext" /> that suppresses
///     all freshness (ETag) checks and generation
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
///     Set automatically when <see cref="SchemataResourceOptions.SuppressFreshness" /> is
///     <see langword="true" />.
/// </summary>
public sealed class FreshnessSuppressed;
