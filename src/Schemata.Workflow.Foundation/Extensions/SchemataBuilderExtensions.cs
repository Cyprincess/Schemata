using System;
using Schemata.Abstractions.Options;
using Schemata.Core;
using Schemata.Mapping.Skeleton.Configurations;
using Schemata.Workflow.Foundation;
using Schemata.Workflow.Foundation.Features;
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
        where TWorkflow : SchemataWorkflow, new()
        where TTransition : SchemataTransition, new() {
        return UseWorkflow<TWorkflow, TTransition, WorkflowResponse>(builder, configure, mapping);
    }

    public static SchemataWorkflowBuilder UseWorkflow<TWorkflow, TTransition, TResponse>(
        this SchemataBuilder                                             builder,
        Action<SchemataWorkflowOptions>?                                 configure = null,
        Action<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>? mapping   = null)
        where TWorkflow : SchemataWorkflow, new()
        where TTransition : SchemataTransition, new()
        where TResponse : WorkflowResponse {
        builder.Configure<SchemataWorkflowOptions>(options => {
            configure?.Invoke(options);

            options.WorkflowType           = typeof(TWorkflow);
            options.WorkflowResponseType   = typeof(TResponse);
            options.TransitionType         = typeof(TTransition);
            options.TransitionResponseType = typeof(TransitionResponse);
        });

        builder.Configure<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>(map => {
            map.For(d => d.Graph).From(s => s.Graph);
            map.For(d => d.Events).From(s => s.Events);
            map.For(d => d.Transitions).From(s => s.Transitions);
            map.For(d => d.Id).From(s => s.Workflow.Id);
            map.For(d => d.State).From(s => s.Instance.State);
            map.For(d => d.CreateTime).From(s => s.Workflow.CreateTime);
            map.For(d => d.UpdateTime).From(s => s.Workflow.UpdateTime);

            mapping?.Invoke(map);
        });

        builder.AddFeature<SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>>();

        return new(builder.Services);
    }
}
