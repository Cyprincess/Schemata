using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Bpmn.Runtime.Compensation;

/// <summary>Executes a BPMN compensation target for a completed host activity.</summary>
public sealed class BoundaryCompensationHandler : ICompensationHandler
{
    private readonly ICompensationExecutor _executor;

    /// <summary>Initializes a boundary-backed compensation handler.</summary>
    /// <param name="activity">The completed activity that registered this handler.</param>
    /// <param name="compensationTarget">The target reached from the compensation boundary event.</param>
    /// <param name="eventName">The boundary event name recorded on the compensation transition.</param>
    /// <param name="executor">The runtime facade used to execute the target activity.</param>
    public BoundaryCompensationHandler(
        Activity               activity,
        FlowElement            compensationTarget,
        string                 eventName,
        ICompensationExecutor? executor = null) {
        if (activity is null) {
            throw new ArgumentNullException(nameof(activity));
        }

        if (compensationTarget is null) {
            throw new ArgumentNullException(nameof(compensationTarget));
        }

        if (eventName is null) {
            throw new ArgumentNullException(nameof(eventName));
        }

        if (executor is null) {
            executor = UnavailableCompensationExecutor.Instance;
        }

        Activity           = activity;
        CompensationTarget = compensationTarget;
        EventName          = eventName;
        _executor          = executor;
    }

    /// <summary>The boundary event name recorded on the compensation transition.</summary>
    public string EventName { get; }

    /// <summary>The completed activity that registered this handler.</summary>
    public Activity Activity { get; }

    /// <summary>The target reached from the compensation boundary event.</summary>
    public FlowElement CompensationTarget { get; }

    /// <summary>Invokes the compensation target through the configured executor.</summary>
    public async ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(context);
        await _executor.ExecuteAsync(Activity, CompensationTarget, EventName, context, ct);
    }

    private sealed class UnavailableCompensationExecutor : ICompensationExecutor
    {
        public static readonly UnavailableCompensationExecutor Instance = new();

        private UnavailableCompensationExecutor() { }

        public ValueTask ExecuteAsync(
            Activity                      activity,
            FlowElement                   compensationTarget,
            string                        eventName,
            CompensationInvocationContext context,
            CancellationToken             ct = default) {
            throw new InvalidOperationException("A BPMN compensation executor is required to invoke a boundary compensation handler.");
        }
    }
}
