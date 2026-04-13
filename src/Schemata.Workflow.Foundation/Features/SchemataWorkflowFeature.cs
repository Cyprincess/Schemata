using Automatonymous.Graphing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Foundation.Features;
using Schemata.Mapping.Skeleton.Configurations;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Managers;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Features;

/// <summary>
///     Feature that registers the workflow manager, type resolver, and mapping configuration.
/// </summary>
/// <typeparam name="TWorkflow">The workflow entity type.</typeparam>
/// <typeparam name="TTransition">The transition entity type.</typeparam>
/// <typeparam name="TResponse">The workflow response DTO type.</typeparam>
[DependsOn<SchemataControllersFeature>]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataWorkflowFeature<TWorkflow, TTransition, TResponse> : FeatureBase
    where TWorkflow : SchemataWorkflow, new()
    where TTransition : SchemataFlowTransition, new()
    where TResponse : WorkflowResponse
{
    public const int DefaultPriority = SchemataMappingFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var mapping = configurators.Pop<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>();

        services.AddOptions<MvcOptions>()
                .Configure<IOptions<SchemataWorkflowOptions>>((mvc, opts) => {
                     mvc.Conventions.Add(new WorkflowControllerConvention(opts.Value.AuthenticationScheme));
                 });

        var part = new SchemataExtensionPart<SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>>();
        services.AddMvcCore().ConfigureApplicationPartManager(manager => {
            manager.ApplicationParts.Add(part);
        });

        services.TryAddSingleton<ITypeResolver, TypeResolver>();

        services.TryAddScoped<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>();
        services.TryAddScoped<IWorkflowManager<TWorkflow, TTransition, TResponse>>(sp => sp.GetRequiredService<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>());
        services.TryAddScoped<IWorkflowManager>(sp => sp.GetRequiredService<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>());

        services.Map<Vertex, string>(map => { map.With(s => s.Title); });

        services.Map(mapping);
    }
}
