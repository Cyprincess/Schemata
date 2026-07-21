namespace Schemata.Abstractions.Resource;

/// <summary>
///     Carries an AIP-160 filter expression for an operation that selects resource collections.
/// </summary>
public interface IFilterRequest
{
    /// <summary>
    ///     The AIP-160 filter expression, or <see langword="null" /> when the operation selects every resource.
    /// </summary>
    string? Filter { get; }
}
