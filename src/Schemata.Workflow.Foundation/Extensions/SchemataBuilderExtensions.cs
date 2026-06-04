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

/// <summary>
///     Extension methods for configuring the workflow subsystem on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the workflow subsystem using default <see cref="SchemataWorkflow" /> and
    ///     <see cref="SchemataFlowTransition" />
    ///     types.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">Optional configuration for workflow options.</param>
    /// <param name="mapping">Optional mapping configuration for the workflow response.</param>
    /// <returns>A <see cref="SchemataWorkflowBuilder" /> for further configuration.</returns>
    public static SchemataWorkflowBuilder UseWorkflow(
        this SchemataBuilder                                                                      builder,
        Action<SchemataWorkflowOptions>?                                                          configure = null,
        Action<Map<WorkflowDetails<SchemataWorkflow, SchemataFlowTransition>, WorkflowResponse>>? mapping   = null
    ) {
        return builder.UseWorkflow<SchemataWorkflow, SchemataFlowTransition>(configure, mapping);
    }

    /// <summary>
    ///     Enables the workflow subsystem with custom workflow and transition types, using the default
    ///     <see cref="WorkflowResponse" />.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
    /// <typeparam name="TTransition">The transition entity type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">Optional configuration for workflow options.</param>
    /// <param name="mapping">Optional mapping configuration for the workflow response.</param>
    /// <returns>A <see cref="SchemataWorkflowBuilder" /> for further configuration.</returns>
    public static SchemataWorkflowBuilder UseWorkflow<TWorkflow, TTransition>(
        this SchemataBuilder                                                    builder,
        Action<SchemataWorkflowOptions>?                                        configure = null,
        Action<Map<WorkflowDetails<TWorkflow, TTransition>, WorkflowResponse>>? mapping   = null
    )
        where TWorkflow : SchemataWorkflow, new()
        where TTransition : SchemataFlowTransition, new() {
        return builder.UseWorkflow<TWorkflow, TTransition, WorkflowResponse>(configure, mapping);
    }

    /// <summary>
    ///     Enables the workflow subsystem with fully custom workflow, transition, and response types.
    /// </summary>
    /// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
    /// <typeparam name="TTransition">The transition entity type.</typeparam>
    /// <typeparam name="TResponse">The workflow response DTO type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">Optional configuration for workflow options.</param>
    /// <param name="mapping">Optional mapping configuration for the workflow response.</param>
    /// <returns>A <see cref="SchemataWorkflowBuilder" /> for further configuration.</returns>
    public static SchemataWorkflowBuilder UseWorkflow<TWorkflow, TTransition, TResponse>(
        this SchemataBuilder                                             builder,
        Action<SchemataWorkflowOptions>?                                 configure = null,
        Action<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>? mapping   = null
    )
        where TWorkflow : SchemataWorkflow, new()
        where TTransition : SchemataFlowTransition, new()
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
            map.For(d => d.Uid).From(s => s.Workflow.Uid);
            map.For(d => d.State).From(s => s.Instance.State);
            map.For(d => d.CreateTime).From(s => s.Workflow.CreateTime);
            map.For(d => d.UpdateTime).From(s => s.Workflow.UpdateTime);

            mapping?.Invoke(map);
        });

        builder.AddFeature<SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>>();

        return new(builder.Services);
    }
}
