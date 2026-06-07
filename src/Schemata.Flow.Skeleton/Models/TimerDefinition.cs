namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Timer event definition. The <see cref="TimeExpression" /> is interpreted
///     according to <see cref="TimerType" />.
/// </summary>
public sealed class TimerDefinition : IEventDefinition
{
    /// <summary>
    ///     Whether the time expression is a fixed date, duration, or cycle.
    /// </summary>
    public TimerType TimerType { get; set; }

    /// <summary>
    ///     The time expression string — ISO 8601 datetime/duration, or cron.
    /// </summary>
    public string TimeExpression { get; set; } = null!;

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
