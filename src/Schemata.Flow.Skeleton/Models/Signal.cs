using System;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Signal event definition for broadcasts delivered to subscribing process instances.
/// </summary>
public class Signal : IEventDefinition
{
    /// <summary>
    ///     The CLR type of the payload carried by this signal.
    /// </summary>
    public Type? PayloadType { get; set; }

    #region IEventDefinition Members

    public string Name { get; set; } = null!;

    #endregion
}

/// <summary>
///     A BPMN Signal event definition carrying a statically declared payload type.
/// </summary>
/// <typeparam name="TPayload">The payload type delivered to typed conditions and procedure tasks.</typeparam>
public sealed class Signal<TPayload> : Signal
{
    public Signal() { PayloadType = typeof(TPayload); }
}
