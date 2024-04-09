using System;
using Schemata.Core;
using Schemata.Mapping.Skeleton.Configurations;
using Schemata.Workflow.Foundation;
using Schemata.Workflow.Foundation.Features;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Models;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataWorkflowBuilder UseWorkflow(
        this SchemataBuilder                                                                  builder,
        Action<SchemataWorkflowOptions>?                                                      configure = null,
        Action<Map<WorkflowDetails<SchemataWorkflow, SchemataTransition>, WorkflowResponse>>? mapping   = null) {
        return UseWorkflow<SchemataWorkflow, SchemataTransition>(builder, configure, mapping);
    }

    public static SchemataWorkflowBuilder UseWorkflow<TWorkflow, TTransition>(
        this SchemataBuilder                                                    builder,
        Action<SchemataWorkflowOptions>?                                        configure = null,
        Action<Map<WorkflowDetails<TWorkflow, TTransition>, WorkflowResponse>>? mapping   = null)
        where TTransition : SchemataTransition
        where TWorkflow : SchemataWorkflow {
        return UseWorkflow<TWorkflow, TTransition, WorkflowResponse>(builder, configure, mapping);
    }

    public static SchemataWorkflowBuilder UseWorkflow<TWorkflow, TTransition, TResponse>(
        this SchemataBuilder                                             builder,
        Action<SchemataWorkflowOptions>?                                 configure = null,
        Action<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>? mapping   = null)
        where TTransition : SchemataTransition
        where TWorkflow : SchemataWorkflow
        where TResponse : WorkflowResponse {
        builder.Configure<SchemataWorkflowOptions>(options => {
            configure?.Invoke(options);

            options.WorkflowType           = typeof(TWorkflow);
            options.WorkflowResponseType   = typeof(TResponse);
            options.TransitionType         = typeof(TTransition);
            options.TransitionResponseType = typeof(TransitionResponse);
        });

        builder.Configure<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>(map => {
            map.For(d => d.Events).From(s => s.Events);
            map.For(d => d.Transitions).From(s => s.Transitions);
            map.For(d => d.Id).From(s => s.Workflow.Id);
            map.For(d => d.State).From(s => s.Instance.State);
            map.For(d => d.CreationDate).From(s => s.Workflow.CreationDate);
            map.For(d => d.ModificationDate).From(s => s.Workflow.ModificationDate);

            mapping?.Invoke(map);
        });

        builder.AddFeature<SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>>();

        return new(builder);
    }
}
