using System;
using System.Collections.Generic;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     The context passed to an <see cref="IConditionExpression.Evaluate" /> invocation during flow
///     traversal. Provides access to the process definition, the addressed token, the event payload,
///     the bookkeeping counters, and the current element name.
/// </summary>
public sealed class FlowConditionContext
{
    /// <summary>The process definition that contains the flow being traversed.</summary>
    public ProcessDefinition Definition { get; set; } = null!;

    /// <summary>
    ///     The token being evaluated. The state-machine engine sets this to the unique token; the
    ///     BPMN engine sets it to the specific token whose outgoing flow is under evaluation.
    /// </summary>
    public TokenSnapshot Token { get; set; } = null!;

    /// <summary>The persisted process instance being evaluated.</summary>
    public SchemataProcess? Process { get; set; }

    /// <summary>The persisted token entity being evaluated.</summary>
    public SchemataProcessToken? TokenEntity { get; set; }

    /// <summary>The execution context shared by condition evaluation and engine persistence.</summary>
    public required FlowExecutionContext Execution { get; set; }

    /// <summary>The event payload visible to typed payload conditions.</summary>
    public object? Payload { get; set; }

    /// <summary>Engine-private counters visible to condition expressions.</summary>
    public Dictionary<string, int> Bookkeeping { get; set; } = [];

    /// <summary>The element name currently being evaluated for outgoing transitions.</summary>
    public string CurrentState { get; set; } = null!;

    /// <summary>Builds a task context for source-aware condition expressions.</summary>
    public FlowTaskContext CreateTaskContext() {
        if (Process is null || TokenEntity is null) {
            throw new InvalidOperationException("Source-aware flow conditions require process and token context.");
        }

        return new(Definition, Process, TokenEntity, Execution, Payload) { TrackSources = false };
    }
}
