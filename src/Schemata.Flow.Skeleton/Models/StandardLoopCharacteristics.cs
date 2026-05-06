using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A standard loop — the activity executes repeatedly while (or until)
///     <see cref="LoopCondition" /> evaluates in the expected direction.
///     See <seealso href="https://www.omg.org/spec/BPMN/2.0.2/">BPMN 2.0.2</seealso> §10.2.5.
/// </summary>
public sealed class StandardLoopCharacteristics : LoopCharacteristics
{
    /// <summary>
    ///     The condition expression evaluated before or after each iteration.
    /// </summary>
    public IConditionExpression? LoopCondition { get; set; }

    /// <summary>
    ///     When <c>true</c>, the condition is evaluated <em>before</em> each iteration
    ///     (while-do). When <c>false</c> (default), it is evaluated <em>after</em>
    ///     each iteration (do-while).
    /// </summary>
    public bool TestBefore { get; set; }

    /// <summary>
    ///     An optional hard upper bound on the number of loop iterations.
    /// </summary>
    public int? LoopMaximum { get; set; }
}
