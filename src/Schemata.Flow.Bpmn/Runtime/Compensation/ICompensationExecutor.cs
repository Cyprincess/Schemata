using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Executes a compensation target on behalf of a deferred BPMN compensation handler.</summary>
public interface ICompensationExecutor
{
    /// <summary>Executes the compensation target and records its compensation transition.</summary>
    /// <param name="activity">The completed activity being compensated.</param>
    /// <param name="compensationTarget">The flow target reached from the compensation boundary event.</param>
    /// <param name="eventName">The boundary event name recorded on the transition.</param>
    /// <param name="context">The compensation invocation payload.</param>
    /// <param name="ct">Cancellation token for the execution.</param>
    ValueTask ExecuteAsync(
        Activity                      activity,
        FlowElement                   compensationTarget,
        string                        eventName,
        CompensationInvocationContext context,
        CancellationToken             ct = default);
}
