namespace Schemata.Abstractions;

/// <summary>
///     Base interface for all features, providing ordering values so the
///     pipeline executor can sort and sequence them correctly.
/// </summary>
public interface IFeature
{
    /// <summary>
    ///     Insertion order during service registration. Lower values run earlier.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Insertion order during application and endpoint pipeline construction.
    ///     Lower values run earlier.
    /// </summary>
    int Priority { get; }
}
