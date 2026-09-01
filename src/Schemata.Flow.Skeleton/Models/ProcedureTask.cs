using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     BPMN task node backed by a delegate that receives the current flow task context.
/// </summary>
public sealed class ProcedureTask : ProcedureTaskBase
{
    /// <summary>The delegate executed when the token enters this task.</summary>
    public Func<FlowTaskContext, ValueTask>? Body { get; set; }

    protected internal override ValueTask InvokeAsync(FlowTaskContext context) {
        return Body?.Invoke(context) ?? ValueTask.CompletedTask;
    }
}

/// <summary>
///     BPMN task node backed by a delegate that also receives a typed event payload.
/// </summary>
/// <typeparam name="TPayload">The payload type accepted by this task.</typeparam>
public sealed class ProcedureTask<TPayload> : ProcedureTaskBase
{
    /// <summary>The delegate executed when the token enters this task.</summary>
    public Func<FlowTaskContext, TPayload, ValueTask>? Body { get; set; }

    protected internal override ValueTask InvokeAsync(FlowTaskContext context) {
        if (Body is null) {
            return ValueTask.CompletedTask;
        }

        if (context.Payload is TPayload payload) {
            return Body(context, payload);
        }

        throw new InvalidOperationException($"Procedure task '{Name}' requires payload type '{typeof(TPayload).FullName}'.");
    }
}
