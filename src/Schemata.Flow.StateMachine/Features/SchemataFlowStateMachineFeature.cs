using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.StateMachine.Features;

/// <summary>Registers the state-machine Flow runtime.</summary>
[DependsOn<SchemataFlowFeature>]
public sealed class SchemataFlowStateMachineFeature : FeatureBase
{
    public override int Order => SchemataFlowFeature.DefaultOrder - 1;

    public override int Priority => SchemataFlowFeature.DefaultPriority + 50_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
    }
}
