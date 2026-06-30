using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Features;

/// <summary>Registers the BPMN Flow runtime and validator.</summary>
[DependsOn<SchemataFlowFeature>]
public sealed class SchemataFlowBpmnFeature : FeatureBase
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
        services.TryAddKeyedSingleton<IFlowRuntime, BpmnEngine>(SchemataConstants.FlowEngines.Bpmn);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, BpmnFlowEngineValidator>());
    }
}
