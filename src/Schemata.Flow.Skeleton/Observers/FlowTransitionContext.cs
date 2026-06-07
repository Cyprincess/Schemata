using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>Per-transition payload handed to <see cref="IFlowTransitionObserver" />.</summary>
public class FlowTransitionContext
{
    /// <summary>The persisted process instance reflecting the post-transition state.</summary>
    public SchemataProcess Process { get; set; } = null!;

    /// <summary>The active process definition. May be <c>null</c> during termination of a deregistered process.</summary>
    public ProcessDefinition? Definition { get; set; }

    /// <summary>Engine-produced view of the transition outcome.</summary>
    public ProcessInstance Instance { get; set; } = null!;

    /// <summary>State identifier before the transition was applied.</summary>
    public string? PreviousState { get; set; }

    /// <summary>
    ///     Waiting element identifier before the transition was applied. Observers that
    ///     react to leaving a waiting state need this because <see cref="Process" /> already
    ///     reflects the post-transition view.
    /// </summary>
    public string? PreviousWaitingAtId { get; set; }

    /// <summary>Waiting element display name before the transition was applied.</summary>
    public string? PreviousWaitingAt { get; set; }

    /// <summary>Event that triggered the transition, if any.</summary>
    public IEventDefinition? Trigger { get; set; }
}
