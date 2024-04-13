using System.Collections.Generic;
using Automatonymous.Graphing;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public sealed class WorkflowDetails<TWorkflow, TTransition>
    where TTransition : SchemataTransition
    where TWorkflow : SchemataWorkflow
{
    public TWorkflow Workflow { get; set; } = null!;

    public IStatefulEntity Instance { get; set; } = null!;

    public StateMachineGraph? Graph { get; set; }

    public List<string>? Events { get; set; }

    public List<TTransition>? Transitions { get; set; }
}
