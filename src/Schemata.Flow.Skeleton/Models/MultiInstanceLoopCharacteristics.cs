using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A multi-instance activity — the activity is instantiated multiple times,
///     either sequentially or in parallel. The number of instances is determined
///     by <see cref="LoopCardinality" />.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> §10.2.5.
/// </summary>
public sealed class MultiInstanceLoopCharacteristics : LoopCharacteristics
{
    /// <summary>
    ///     The expression that determines how many instances to create.
    /// </summary>
    public IConditionExpression? LoopCardinality { get; set; }

    /// <summary>
    ///     An optional condition that can trigger early completion of all instances.
    /// </summary>
    public IConditionExpression? CompletionCondition { get; set; }

    /// <summary>
    ///     When <c>true</c>, instances execute one at a time.
    ///     When <c>false</c>, instances execute in parallel.
    /// </summary>
    public bool IsSequential { get; set; }

    /// <summary>
    ///     Determines how individual instance-completion events are aggregated
    ///     into the overall activity completion.
    /// </summary>
    public MIEventBehavior OneCompletedEventBehavior { get; set; }
}
