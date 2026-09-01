using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton.Advisors;
using CompleteActivityRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Actor.Tests.Fixtures;

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
        AdviceContext                               ctx,
        CompleteActivityRequest                     request,
        RequestHandlerContinuation<ProcessSnapshot> next,
        CancellationToken                           ct = default) {
        Interlocked.Increment(ref _invocationCount);

        return next(ct);
    }
}