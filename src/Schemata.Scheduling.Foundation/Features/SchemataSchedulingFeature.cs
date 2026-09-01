using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Features;

/// <summary>Registers the in-memory <see cref="IScheduler" /> and the audit lifecycle observer.</summary>
public sealed class SchemataSchedulingFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling feature.</summary>
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 80_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataScheduling();
}
