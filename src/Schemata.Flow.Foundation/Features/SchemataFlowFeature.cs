using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Flow.Foundation.Features;

/// <summary>Registers the BPMN process registry, lifecycle notifier, and resource-method handlers.</summary>
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
    ) => services.AddSchemataFlow();
}
