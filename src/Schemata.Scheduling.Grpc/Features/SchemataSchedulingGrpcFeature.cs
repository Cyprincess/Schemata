using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Features;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton.Entities;

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

        var resources = new SchemataResourceBuilder(schemata, services);
        resources.Use<SchemataJob, SchemataJob, SchemataJob, SchemataJob>(
            [GrpcResourceAttribute.Name],
            resource => resource.Methods = SchedulingResourceRegistration.JobMethods);
        resources.Use<SchemataJobExecution, Operation, Operation, Operation>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = SchedulingResourceRegistration.ExecutionOperations;
                resource.Methods    = SchedulingResourceRegistration.ExecutionMethods;
            });
    }
}
