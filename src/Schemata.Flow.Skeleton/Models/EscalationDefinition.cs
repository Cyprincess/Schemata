namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Escalation event definition. Unlike <see cref="ErrorDefinition" />,
///     escalations may be non-interrupting and are typically handled by a parent scope.
/// </summary>
public sealed class EscalationDefinition : IEventDefinition
{
    /// <summary>
    ///     An optional escalation code for matching.
    /// </summary>
    public string? EscalationCode { get; set; }

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
