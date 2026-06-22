using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation.Features;

/// <summary>Registers the BPMN process engine, registry, runtime, and lifecycle observers.</summary>
public sealed class SchemataFlowFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Order" /> for the Flow feature.</summary>
    public const int DefaultOrder = DefaultPriority;

    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 80_000_000;

    public override int Order => DefaultOrder;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddSingleton<IProcessRegistry>(sp => {
            var registry = ActivatorUtilities.CreateInstance<ProcessRegistry>(sp);
            var configs  = sp.GetRequiredService<IOptions<SchemataFlowOptions>>().Value.Configurations;
            foreach (var config in configs) {
                registry.RegisterAsync(config, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }

            return registry;
        });
        services.TryAddSingleton<IProcessRuntime, ProcessRuntime>();

        services.AddHostedService<ProcessInitializer>();
    }
}
