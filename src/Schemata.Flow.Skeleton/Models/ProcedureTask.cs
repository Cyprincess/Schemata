using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     BPMN task node backed by a delegate that receives the current flow task context.
/// </summary>
public sealed class ProcedureTask : ProcedureTaskBase
{
    private Func<FlowTaskContext, CancellationToken, ValueTask>? _body;

    /// <summary>The delegate executed when the token enters this task.</summary>
    public Func<FlowTaskContext, CancellationToken, ValueTask>? Body {
        get => _body;
        set {
            EnsureMutable();
            _body = value;
        }
    }

    protected internal override ValueTask InvokeAsync(FlowTaskContext context, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        return Body?.Invoke(context, ct) ?? ValueTask.CompletedTask;
    }
}

/// <summary>
///     BPMN task node backed by a delegate that also receives a typed event payload.
/// </summary>
/// <typeparam name="TPayload">The payload type accepted by this task.</typeparam>
public sealed class ProcedureTask<TPayload> : ProcedureTaskBase
{
    private Func<FlowTaskContext, TPayload, CancellationToken, ValueTask>? _body;

    /// <summary>The delegate executed when the token enters this task.</summary>
    public Func<FlowTaskContext, TPayload, CancellationToken, ValueTask>? Body {
        get => _body;
        set {
            EnsureMutable();
            _body = value;
        }
    }

    protected internal override ValueTask InvokeAsync(FlowTaskContext context, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        if (Body is null) {
            return ValueTask.CompletedTask;
        }

        if (context.Payload is TPayload payload) {
            return Body(context, payload, ct);
        }

        throw new InvalidOperationException($"Procedure task '{Name}' requires payload type '{typeof(TPayload).FullName}'.");
    }
}
