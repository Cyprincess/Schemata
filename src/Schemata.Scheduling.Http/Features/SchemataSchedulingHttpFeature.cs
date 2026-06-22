using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Http.Features;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Scheduling.Http.Features;

/// <summary>Registers Scheduling resources for the HTTP resource transport.</summary>
[DependsOn<SchemataSchedulingFeature>]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataSchedulingHttpFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling HTTP feature.</summary>
    public const int DefaultPriority = SchemataSchedulingFeature.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        SchedulingResourceRegistration.RegisterHandlers(services);
        SchedulingResourceRegistration.RegisterMethods(new(schemata, services), HttpResourceAttribute.Name);
    }
}
