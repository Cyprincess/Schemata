using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>Per-transition payload handed to <see cref="IFlowTransitionAdvisor" />.</summary>
public class FlowTransitionContext
{
    /// <summary>The active process definition. May be <c>null</c> during termination of a deregistered process.</summary>
    public ProcessDefinition? Definition { get; set; }

    /// <summary>
    ///     Engine-produced view of the transition outcome, carrying the new token set and transition rows.
    ///     The mutated process row lives at <c>Snapshot.Process</c>.
    /// </summary>
    public ProcessSnapshot Snapshot { get; set; } = null!;

    /// <summary>
    ///     The specific token this transition advances. Required for both engines: the state-machine
    ///     engine sets it to the unique token; the BPMN engine sets it to the affected token.
    /// </summary>
    public required TokenSnapshot Token { get; init; }

    /// <summary>
    ///     Waiting element name before the transition was applied. Advisors that react to
    ///     leaving a waiting state read it from here, since <see cref="Snapshot" /> carries the new
    ///     waiting element on its token rows.
    /// </summary>
    public string? PreviousWaitingAtName { get; set; }

    /// <summary>
    ///     Unit of work the transition is committing through. Advisors that need their writes to
    ///     commit atomically with the process row enlist their repositories with
    ///     <see cref="IRepository.Join" />; advisors that only touch external systems may ignore it.
    ///     Set by the runtime just before the advisor pipeline runs.
    /// </summary>
    public IUnitOfWork? UnitOfWork { get; set; }
}
