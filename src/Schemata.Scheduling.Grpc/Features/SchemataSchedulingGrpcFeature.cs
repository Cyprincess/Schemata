using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Building;
using Schemata.Resource.Grpc.Features;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Grpc.Features;

/// <summary>Registers Scheduling resources for the gRPC resource transport.</summary>
/// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
/// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
[DependsOn<SchemataSchedulingFeature>]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataSchedulingGrpcFeature<TJob, TExecution> : FeatureBase
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
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
        SchedulingResourceRegistration<TJob, TExecution>.RegisterHandlers(services);

        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(SchedulingResourceRegistration<TJob, TExecution>.AuthenticationSchemeKey),
        };
        resources.Use<TJob, TJob, TJob, TJob>(
            [GrpcResourceAttribute.Name],
            resource => resource.Methods = SchedulingResourceRegistration<TJob, TExecution>.JobMethods);
        resources.Use<TExecution, Operation, Operation, Operation>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = SchedulingResourceRegistration<TJob, TExecution>.ExecutionOperations;
                resource.Methods    = SchedulingResourceRegistration<TJob, TExecution>.ExecutionMethods;
            });
    }
}
