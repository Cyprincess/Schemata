using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton;

public class WorkflowRequestContext<TRequest>
{
    public string? Operation { get; set; }

    public TRequest? Request { get; set; }

    public SchemataWorkflow? Workflow { get; set; }
}
