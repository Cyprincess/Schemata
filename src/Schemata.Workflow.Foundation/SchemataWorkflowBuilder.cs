using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Workflow.Foundation.Advices;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Foundation;

public sealed class SchemataWorkflowBuilder
{
    public SchemataWorkflowBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }

    public SchemataWorkflowBuilder WithAuthorization() {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowSubmitAdvice, AdviceSubmitAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowGetAdvice, AdviceGetAuthorize>());
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowRaiseAdvice, AdviceRaiseAuthorize>());

        return this;
    }

    public SchemataWorkflowBuilder Use<TStateMachine, TI>() where TStateMachine : StateMachineBase<TI>
                                                            where TI : class, IStatefulEntity {
        Services.TryAddScoped<StateMachineBase<TI>, TStateMachine>();

        return this;
    }
}
