using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Grpc.Features;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;

namespace Schemata.Scheduling.Grpc.Features;

/// <summary>Registers Scheduling resources for the gRPC resource transport.</summary>
[DependsOn<SchemataSchedulingFeature>]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataSchedulingGrpcFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Scheduling gRPC feature.</summary>
    public const int DefaultPriority = SchemataSchedulingFeature.DefaultPriority + 300_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        SchedulingResourceRegistration.RegisterHandlers(services);
        SchedulingResourceRegistration.RegisterMethods(new(schemata, services), GrpcResourceAttribute.Name);
    }
}
