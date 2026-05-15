using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Scheduling.Foundation.Internal;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Scheduling.Foundation.Features;

[DependsOn("Schemata.Entity.EntityFrameworkCore.Features.SchemataEntityFeature", Optional = true)]
[DependsOn("Schemata.Entity.LinqToDB.Features.SchemataEntityFeature", Optional = true)]
public sealed class SchemataSchedulingFeature : FeatureBase
{
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 25_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddSingleton<IScheduler, DefaultScheduler>();
        services.AddHostedService<SchedulingInitializer>();
    }
}
