using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Invokes the BPMN compensation activity that reverts a completed activity.</summary>
public interface ICompensationHandler
{
    /// <summary>The activity whose completion this handler reverts.</summary>
    Activity Activity { get; }

    /// <summary>The compensation flow target, usually the target reached from the boundary event's outgoing flow.</summary>
    FlowElement CompensationTarget { get; }

    /// <summary>Invokes the compensation activity. Failures propagate to the compensation coordinator result.</summary>
    /// <param name="context">The compensation invocation payload.</param>
    /// <param name="ct">Cancellation token for the invocation.</param>
    ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default);
}
