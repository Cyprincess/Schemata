using System;

namespace Schemata.Workflow.Skeleton;

/// <summary>
/// Configuration options for the workflow subsystem, specifying the concrete types used for workflows, transitions, and responses.
/// </summary>
public sealed class SchemataWorkflowOptions
{
    /// <summary>
    /// The concrete <see cref="Schemata.Workflow.Skeleton.Entities.SchemataWorkflow"/> type used by the application.
    /// </summary>
    public Type WorkflowType { get; set; } = null!;

    /// <summary>
    /// The response type returned when mapping workflow details.
    /// </summary>
    public Type WorkflowResponseType { get; set; } = null!;

    /// <summary>
    /// The concrete <see cref="Schemata.Workflow.Skeleton.Entities.SchemataTransition"/> type used by the application.
    /// </summary>
    public Type TransitionType { get; set; } = null!;

    /// <summary>
    /// The response type used when mapping transition records.
    /// </summary>
    public Type TransitionResponseType { get; set; } = null!;

    /// <summary>
    /// An optional package identifier for scoping workflow configurations.
    /// </summary>
    public string? Package { get; set; }
}
