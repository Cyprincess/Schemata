using System;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Skeleton;

public class SchemataWorkflowOptions
{
    public Type WorkflowType { get; set; } = typeof(SchemataWorkflow);

    public Type WorkflowResponseType { get; set; } = typeof(WorkflowResponse);

    public Type TransitionType { get; set; } = typeof(SchemataTransition);

    public Type TransitionResponseType { get; set; } = typeof(TransitionResponse);
}
