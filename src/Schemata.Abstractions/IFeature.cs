namespace Schemata.Abstractions;

/// <summary>
///     Base interface for all features and advisors, providing ordering and priority for pipeline execution.
/// </summary>
public interface IFeature
{
    /// <summary>
    ///     Gets the execution order used to sort features during service configuration.
    /// </summary>
    int Order { get; }

    /// <summary>
    ///     Gets the priority used to sort features during application and endpoint configuration.
    /// </summary>
    int Priority { get; }
}
