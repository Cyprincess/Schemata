using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;
using Schemata.Messaging.Skeleton.Advisors;

namespace Schemata.Flow.Actor.Tests.Fixtures;

/// <summary>
///     Single-activity state-machine process: one live token sits at <see cref="Doing" /> until a
///     <see cref="CompleteActivityRequest" /> advances it — parking it at its own gateway, the
///     state machine's normal shape for a task awaiting an external correlation (RFC state-machine
///     semantics; see <c>NoneTaskProgressionShould.Advance_Parks_None_Task_At_Its_Gateway</c>).
///     That first advance is the only one that produces a real
///     <see cref="Schemata.Flow.Skeleton.Entities.SchemataProcessTransition" /> row; every other
///     concurrent attempt reloads the token, finds it already parked (<c>WaitingAtName</c> set),
///     and returns the current snapshot unchanged — the engine's own idempotent no-op, not a race
///     artifact. <see cref="Approved" /> is never actually correlated by this suite; only the first
///     advance's parking transition is exercised.
/// </summary>
public sealed class ConcurrentActivityProcess : ProcessDefinition
{
    public ConcurrentActivityProcess() {
        this.Start().Go(Doing);
        this.During(Doing).Await(this.On(Approved).Go(Done));
        this.During(Done).End();
    }

    public UserTask Doing    { get; } = null!;
    public UserTask Done     { get; } = null!;
    public Message  Approved { get; } = null!;
}

/// <summary>
///     Answers for every <see cref="FlowCatchKind" /> without actually delivering any of them, so a
///     token may legally park on <see cref="ConcurrentActivityProcess.Approved" /> without the
///     engine's fail-closed "nobody would ever deliver this" check rejecting the transition. The
///     concurrency suite never correlates the message, so <see cref="ArmAsync" /> has nothing to do.
/// </summary>
public sealed class PermissiveFlowCatchHandler : IFlowCatchHandler
{
    public bool Handles(FlowCatchKind kind) { return true; }

    public ValueTask ArmAsync(FlowTransitionContext context, CancellationToken ct = default) { return ValueTask.CompletedTask; }
}

/// <summary>
///     Records every dispatch-time invocation of the <see cref="CompleteActivityRequest" /> command
///     advisor chain, so a test can assert the chain ran exactly once per caller-side dispatch and
///     never again inside the actor's turn.
/// </summary>
public sealed class RecordingCompleteActivityAdvisor : IRequestPipelineAdvisor<CompleteActivityRequest, ProcessSnapshot>
{
    private int _invocationCount;

    public int Order => 0;

    /// <summary>Total number of times this advisor has run across every dispatch so far.</summary>
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public Task<ProcessSnapshot> AdviseAsync(
        AdviceContext                              ctx,
        CompleteActivityRequest                    request,
        RequestHandlerContinuation<ProcessSnapshot> next,
        CancellationToken                          ct = default) {
        Interlocked.Increment(ref _invocationCount);

        return next(ct);
    }
}
