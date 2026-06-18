namespace Schemata.Abstractions;

/// <summary>
///     Base interface for all features, providing ordering values the
///     pipeline executor uses to sort and sequence them.
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
