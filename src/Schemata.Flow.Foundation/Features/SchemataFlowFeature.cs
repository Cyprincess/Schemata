using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;

namespace Schemata.Flow.Foundation.Features;

public sealed class SchemataFlowFeature : FeatureBase
{
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 35_000_000;

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

        services.TryAddSingleton<IProcessRegistry, ProcessRegistry>();
        services.TryAddScoped<IProcessRuntime, ProcessRuntime>();

        services.TryAddKeyedSingleton<IFlowRuntime, StateMachineEngine>(
            SchemataConstants.FlowEngines.StateMachine
        );

        services.AddHostedService<FlowInitializer>();
    }
}
