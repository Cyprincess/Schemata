using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Workflow.Foundation.Advisors;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation;

public sealed class SchemataWorkflowBuilder
{
    public SchemataWorkflowBuilder(IServiceCollection services) { Services = services; }

    public IServiceCollection Services { get; }

    public SchemataWorkflowBuilder WithAuthorization() {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSubmitAdvisor, AdviceWorkflowSubmitAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowGetAdvisor, AdviceWorkflowGetAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowRaiseAdvisor, AdviceWorkflowRaiseAuthorize>());

        return this;
    }

    public SchemataWorkflowBuilder Use<TStateMachine, TI>()
        where TStateMachine : StateMachineBase<TI>
        where TI : class, IStatefulEntity {
        Services.TryAddScoped<StateMachineBase<TI>, TStateMachine>();

        return this;
    }
}
