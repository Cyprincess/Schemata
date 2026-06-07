using Schemata.Abstractions.Advisors;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Advisors;

public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>;

public class FlowTransitionContext
{
    public SchemataProcess Process { get; set; } = null!;

    public ProcessDefinition? Definition { get; set; }

    public ProcessInstance Instance { get; set; } = null!;

    public string? PreviousState { get; set; }

    public IEventDefinition? Trigger { get; set; }
}
