using System;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Message event definition representing directed point-to-point communication.
/// </summary>
public sealed class Message : IEventDefinition
{
    /// <summary>
    ///     The CLR type of the payload carried by this message.
    ///     Used for typed condition evaluation in <see cref="Schemata.Flow.Skeleton.Builders.Branch" /> expressions.
    /// </summary>
    public Type? PayloadType { get; set; }

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
