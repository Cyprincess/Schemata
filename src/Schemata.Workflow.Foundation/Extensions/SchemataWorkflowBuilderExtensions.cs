using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Workflow.Foundation;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataWorkflowBuilderExtensions
{
    public static SchemataWorkflowBuilder Use<TStateMachine, TI>(this SchemataWorkflowBuilder builder)
        where TStateMachine : StateMachineBase<TI>
        where TI : class, IStatefulEntity {
        builder.Services.Add(services => {
            services.TryAddScoped<StateMachineBase<TI>, TStateMachine>();
        });

        return builder;
    }
}
