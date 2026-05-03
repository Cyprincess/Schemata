namespace Schemata.Entity.Repository.Advisors;

/// <summary>
///     Context flag that suppresses concurrency-stamp generation and verification, per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
public sealed class ConcurrencySuppressed;
