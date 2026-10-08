using System;
using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Message event definition representing directed point-to-point communication.
/// </summary>
public class Message : FlowGraphNode, IEventDefinition
{
    private Type? _payloadType;
    private string _name = null!;

    /// <summary>
    ///     The CLR type of the payload carried by this message.
    ///     Used for typed condition evaluation in <see cref="Schemata.Flow.Skeleton.Builders.Branch" /> expressions.
    /// </summary>
    public Type? PayloadType {
        get => _payloadType;
        set {
            EnsureMutable();
            _payloadType = value;
        }
    }

    #region IEventDefinition Members

    public string Name {
        get => _name;
        set {
            EnsureMutable();
            _name = value;
        }
    }

    #endregion

    #region IDescriptive Members

    public string?                      DisplayName  { get; set; }
    public Dictionary<string, string?>? DisplayNames { get; set; }
    public string?                      Description  { get; set; }
    public Dictionary<string, string?>? Descriptions { get; set; }

    #endregion
}

/// <summary>
///     A BPMN Message event definition carrying a statically declared payload type.
/// </summary>
/// <typeparam name="TPayload">The payload type delivered to typed conditions and procedure tasks.</typeparam>
public sealed class Message<TPayload> : Message
{
    public Message() { PayloadType = typeof(TPayload); }
}
