using Automatonymous;
using Schemata.Workflow.Skeleton;

namespace Schemata.Workflow.Tests;

public class OrderStateMachine : StateMachineBase<Order>
{
    public OrderStateMachine() {
        InstanceState(x => x.State);

        Initially(When(Pay).TransitionTo(Progressing),
                  When(Void).TransitionTo(Voided),
                  When(Cancel).TransitionTo(Expired));

        During(Progressing,
               When(Accept).TransitionTo(Final),
               When(Deny).TransitionTo(Denied),
               When(Void).TransitionTo(Voided),
               When(Cancel).TransitionTo(Expired));
    }

    public State Progressing { get; } = null!;
    public State Voided      { get; } = null!;
    public State Expired     { get; } = null!;
    public State Denied      { get; } = null!;

    public Event<OrderEvent> Pay    { get; } = null!;
    public Event<OrderEvent> Accept { get; } = null!;
    public Event<OrderEvent> Void   { get; } = null!;
    public Event<OrderEvent> Cancel { get; } = null!;
    public Event<OrderEvent> Deny   { get; } = null!;
}
