using System;

namespace Schemata.Abstractions.Options;

public sealed class SchemataWorkflowOptions
{
    public Type WorkflowType { get; set; } = null!;

    public Type WorkflowResponseType { get; set; } = null!;

    public Type TransitionType { get; set; } = null!;

    public Type TransitionResponseType { get; set; } = null!;
}
