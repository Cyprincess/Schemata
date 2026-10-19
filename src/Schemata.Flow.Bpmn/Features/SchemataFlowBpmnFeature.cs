using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Features;

/// <summary>Registers the BPMN Flow runtime and validator.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
public sealed class SchemataFlowBpmnFeature<TProcess, TToken, TTransition> : FeatureBase
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
{
    /// <summary>Default <see cref="FeatureBase.Order" /> for the Flow BPMN feature.</summary>
    public const int DefaultOrder = SchemataFlowFeature.DefaultOrder - 1;

    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow BPMN feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 60_000;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddKeyedSingleton<IFlowRuntime<TProcess, TToken, TTransition>, BpmnEngine<TProcess, TToken, TTransition>>(FlowConstants.Engines.Bpmn);
        services.TryAddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.Bpmn,
            (sp, _) => sp.GetRequiredKeyedService<IFlowRuntime<TProcess, TToken, TTransition>>(FlowConstants.Engines.Bpmn));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());
    }
}
