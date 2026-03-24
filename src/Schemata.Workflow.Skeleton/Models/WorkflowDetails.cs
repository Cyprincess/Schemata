using System.Collections.Generic;
using Automatonymous.Graphing;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
/// Aggregates workflow data including the workflow entity, its stateful instance, state graph, available events, and transition history.
/// </summary>
/// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
/// <typeparam name="TTransition">The transition entity type.</typeparam>
/// <remarks>
/// Used as the mapping source when constructing workflow response DTOs.
/// </remarks>
public sealed class WorkflowDetails<TWorkflow, TTransition>
    where TWorkflow : SchemataWorkflow
    where TTransition : SchemataTransition
{
    /// <summary>
    /// The workflow entity.
    /// </summary>
    public TWorkflow Workflow { get; set; } = null!;

    /// <summary>
    /// The stateful entity instance tracked by this workflow.
    /// </summary>
    public IStatefulEntity Instance { get; set; } = null!;

    /// <summary>
    /// The state machine graph showing all states and transitions.
    /// </summary>
    public StateMachineGraph? Graph { get; set; }

    /// <summary>
    /// The names of events available from the current state.
    /// </summary>
    public List<string>? Events { get; set; }

    /// <summary>
    /// The recorded transition history for this workflow.
    /// </summary>
    public List<TTransition>? Transitions { get; set; }
}
