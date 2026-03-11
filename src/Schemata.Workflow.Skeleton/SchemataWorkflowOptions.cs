using System;

namespace Schemata.Workflow.Skeleton;

public sealed class SchemataWorkflowOptions
{
    public Type WorkflowType { get; set; } = null!;

    public Type WorkflowResponseType { get; set; } = null!;

    public Type TransitionType { get; set; } = null!;

    public Type TransitionResponseType { get; set; } = null!;

    public string? Package { get; set; }
}
