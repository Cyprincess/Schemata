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

    public State Progressing { get; private set; } = null!;
    public State Voided      { get; private set; } = null!;
    public State Expired     { get; private set; } = null!;
    public State Denied      { get; private set; } = null!;

    public Event<OrderEvent> Pay    { get; private set; } = null!;
    public Event<OrderEvent> Accept { get; private set; } = null!;
    public Event<OrderEvent> Void   { get; private set; } = null!;
    public Event<OrderEvent> Cancel { get; private set; } = null!;
    public Event<OrderEvent> Deny   { get; private set; } = null!;
}
