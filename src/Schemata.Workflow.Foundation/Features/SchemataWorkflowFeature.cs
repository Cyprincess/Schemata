using Automatonymous.Graphing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Skeleton.Configurations;
using Schemata.Workflow.Skeleton;
using Schemata.Workflow.Skeleton.Entities;
using Schemata.Workflow.Skeleton.Managers;
using Schemata.Workflow.Skeleton.Models;

namespace Schemata.Workflow.Foundation.Features;

[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataJsonSerializerFeature>]
[Information("Workflow depends on Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
[Information("Workflow depends on JsonSerializer feature, it will be added automatically.", Level = LogLevel.Debug)]
public sealed class SchemataWorkflowFeature<TWorkflow, TTransition, TResponse> : FeatureBase
    where TTransition : SchemataTransition, new()
    where TWorkflow : SchemataWorkflow, new()
    where TResponse : WorkflowResponse
{
    public override int Priority => 350_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var mapping = configurators.Pop<Map<WorkflowDetails<TWorkflow, TTransition>, TResponse>>();

        var part = new SchemataExtensionPart<SchemataWorkflowFeature<TWorkflow, TTransition, TResponse>>();
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => { manager.ApplicationParts.Add(part); });

        services.TryAddSingleton<ITypeResolver, TypeResolver>();

        services.TryAddScoped<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>();
        services.TryAddTransient<IWorkflowManager<TWorkflow, TTransition, TResponse>>(sp => sp.GetRequiredService<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>());
        services.TryAddTransient<IWorkflowManager>(sp => sp.GetRequiredService<SchemataWorkflowManager<TWorkflow, TTransition, TResponse>>());

        services.Map<Vertex, string>(map => {
            map.With(s => s.Title);
        });

        services.Map(mapping);
    }
}
