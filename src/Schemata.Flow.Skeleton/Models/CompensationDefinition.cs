namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Compensation event definition — triggers or throws compensation
///     for the activity referenced by <see cref="ActivityRef" />.
/// </summary>
public sealed class CompensationDefinition : IEventDefinition
{
    /// <summary>
    ///     The activity whose compensation handler should be invoked.
    /// </summary>
    public Activity? ActivityRef { get; set; }

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
