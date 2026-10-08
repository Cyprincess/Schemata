using System;
using System.Collections.Generic;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A BPMN Error event definition. Always interrupting.
///     Matched by <see cref="ExceptionType" /> during <see cref="Schemata.Flow.Skeleton.Builders.BoundaryCatch" />
///     resolution.
/// </summary>
public sealed class ErrorDefinition : FlowGraphNode, IEventDefinition
{
    private string? _errorCode;
    private Type _exceptionType = null!;
    private string _name = null!;

    /// <summary>
    ///     An optional BPMN error code used alongside the exception type for matching.
    /// </summary>
    public string? ErrorCode {
        get => _errorCode;
        set {
            EnsureMutable();
            _errorCode = value;
        }
    }

    /// <summary>
    ///     The CLR exception type that triggers this error boundary event.
    /// </summary>
    public Type ExceptionType {
        get => _exceptionType;
        set {
            EnsureMutable();
            _exceptionType = value;
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
