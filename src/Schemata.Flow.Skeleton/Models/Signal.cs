using System;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Signal event definition for broadcasts delivered to subscribing process instances.
/// </summary>
public sealed class Signal : IEventDefinition
{
    /// <summary>
    ///     The CLR type of the payload carried by this signal.
    /// </summary>
    public Type? PayloadType { get; set; }

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}
