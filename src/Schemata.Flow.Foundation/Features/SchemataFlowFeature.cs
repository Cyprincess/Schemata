using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;

namespace Schemata.Flow.Foundation.Features;

/// <summary>Registers the BPMN process engine, registry, runtime, and lifecycle observers.</summary>
public sealed class SchemataFlowFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority"/> for the Flow feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 80_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var builder   = new FlowBuilder();
        var configure = configurators.PopOrDefault<FlowBuilder>();
        configure(builder);

        var flows = builder.Build();

        services.Configure<SchemataFlowOptions>(options => {
            options.Configurations.AddRange(flows);
        });

        services.TryAddSingleton<IProcessRegistry>(sp => {
            var registry = ActivatorUtilities.CreateInstance<ProcessRegistry>(sp);
            var configs  = sp.GetRequiredService<IOptions<SchemataFlowOptions>>().Value.Configurations;
            foreach (var config in configs) {
                registry.RegisterAsync(config, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            return registry;
        });
        services.TryAddSingleton<IProcessRuntime, ProcessRuntime>();

        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(SchemataConstants.FlowEngines.StateMachine);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFlowEngineValidator, StateMachineFlowEngineValidator>());

        services.AddHostedService<ProcessInitializer>();
    }
}
