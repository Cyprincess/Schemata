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

namespace Schemata.Flow.StateMachine.Features;

/// <summary>Registers the state-machine Flow runtime.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
public sealed class SchemataFlowStateMachineFeature<TProcess, TToken, TTransition> : FeatureBase
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
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
        services.TryAddKeyedSingleton<IFlowRuntime<TProcess, TToken, TTransition>, StateMachineEngine<TProcess, TToken, TTransition>>(FlowConstants.Engines.StateMachine);
        services.TryAddKeyedSingleton<IFlowRuntime>(FlowConstants.Engines.StateMachine,
            (sp, _) => sp.GetRequiredKeyedService<IFlowRuntime<TProcess, TToken, TTransition>>(FlowConstants.Engines.StateMachine));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());
    }
}
