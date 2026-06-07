using System;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Error event definition. Always interrupting.
///     Matched by <see cref="ExceptionType" /> during <see cref="Schemata.Flow.Skeleton.Builders.BoundaryCatch" />
///     resolution.
/// </summary>
public sealed class ErrorDefinition : IEventDefinition
{
    /// <summary>
    ///     An optional BPMN error code for matching without relying on the exception type.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    ///     The CLR exception type that triggers this error boundary event.
    /// </summary>
    public Type ExceptionType { get; set; } = null!;

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
