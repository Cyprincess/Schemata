using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>
///     Side-effect hook for <see cref="IProcessRuntime" /> transitions. Observers
///     see the full runtime view (definition, instance, trigger) in a
///     <see cref="FlowTransitionContext" />.
/// </summary>
public interface IFlowTransitionObserver
{
    /// <summary>Fires after a transition is applied to a process instance.</summary>
    Task OnTransitionedAsync(FlowTransitionContext context, CancellationToken ct = default);
}
