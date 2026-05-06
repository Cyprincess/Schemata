using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     Specifies how a multi-instance activity's completion events are aggregated.
/// </summary>
public enum MIEventBehavior
{
    /// <summary>No aggregation — each instance fires independently.</summary>
    None,

    /// <summary>The activity completes as soon as one instance finishes.</summary>
    One,

    /// <summary>The activity completes after all instances finish.</summary>
    All,

    /// <summary>Completion is determined by a custom <see cref="IConditionExpression" />.</summary>
    Complex,
}
