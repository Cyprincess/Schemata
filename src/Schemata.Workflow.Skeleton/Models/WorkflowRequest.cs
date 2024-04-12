using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public class WorkflowRequest<TI>
    where TI : class, IStateful
{
    public virtual string? Type { get; set; }

    public virtual TI? Instance { get; set; }
}
