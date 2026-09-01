using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Observers;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Actor.Tests.Fixtures;

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