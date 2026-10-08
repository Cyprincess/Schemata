namespace Schemata.Abstractions.Resource;

/// <summary>
///     Controls how the list response's <c>total_size</c> is computed per
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>.
///     Counting every list call costs an extra query on large collections; <c>total_size</c>
///     itself is optional, so hosts can trade accuracy for throughput.
/// </summary>
public enum TotalSizeMode
{
    /// <summary>
    ///     Inherit: a per-resource value falls back to the global option, which
    ///     falls back to <see cref="Exact" />.
    /// </summary>
    Default,

    /// <summary>
    ///     Omit <c>total_size</c> and skip the count query.
    /// </summary>
    None,

    /// <summary>
    ///     Use the repository's estimate of the filtered query. Omit <c>total_size</c> when
    ///     estimation is unavailable or filtering requires local residual evaluation; exact counting is skipped.
    /// </summary>
    Estimated,

    /// <summary>
    ///     Count the matching rows on every list call.
    /// </summary>
    Exact,
}
