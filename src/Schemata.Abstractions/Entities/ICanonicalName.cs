namespace Schemata.Abstractions.Entities;

/// <summary>
///     Provides a fully-qualified resource name following
///     <seealso href="https://google.aip.dev/122">AIP-122: Resource names</seealso>.
/// </summary>
public interface ICanonicalName
{
    /// <summary>
    ///     The base identifier used internally by the framework to construct the
    ///     canonical name (e.g., the <c>{name}</c> segment in
    ///     <c>{package}/{entity}/{name}</c> per AIP-122).
    /// </summary>
    string? Name { get; set; }

    /// <summary>
    ///     The collection-relative resource name
    ///     (e.g., <c>"publishers/acme/books/les-miserables"</c>).
    /// </summary>
    string? CanonicalName { get; set; }
}
