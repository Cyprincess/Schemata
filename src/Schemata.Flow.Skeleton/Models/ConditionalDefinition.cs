using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Conditional event definition — triggers when <see cref="Condition" />
///     evaluates to <c>true</c>.
/// </summary>
public sealed class ConditionalDefinition : IEventDefinition
{
    /// <summary>
    ///     The condition expression that must become <c>true</c> for this event to trigger.
    /// </summary>
    public IConditionExpression Condition { get; set; } = null!;

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
