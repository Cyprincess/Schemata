using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Features;
using Schemata.Scheduling.Foundation;
using Schemata.Scheduling.Foundation.Features;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Scheduling.Grpc.Features;

/// <summary>Registers Scheduling resources for the gRPC resource transport.</summary>
[DependsOn<SchemataSchedulingFeature>]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataSchedulingGrpcFeature : FeatureBase
{
    public const int DefaultPriority = SchemataSchedulingFeature.DefaultPriority + 300_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        RegisterHandlers(services);
        RegisterResources(new(schemata, services));
    }

    private static void RegisterHandlers(IServiceCollection services) {
        services.TryAddScoped<RunJobHandler>();
        services.TryAddScoped<CancelOperationHandler>();
        services.TryAddScoped<WaitOperationHandler>();
        services.Map<SchemataJobExecution, SchemataOperation>(map => map.With(e => SchemataOperation.FromExecution(e)));
        services.AddSchedulerOperationDispatcher();
    }

    private static void RegisterResources(SchemataResourceBuilder resources) {
        resources.Use<SchemataJob, SchemataJob, SchemataJob, SchemataJob>(
            [GrpcResourceAttribute.Name],
            resource => resource.Methods = [
                new(Verbs.Run, typeof(RunJobHandler)),
            ]);

        resources.Use<SchemataJobExecution, SchemataOperation, SchemataOperation, SchemataOperation>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = [Operations.Get, Operations.List, Operations.Delete];
                resource.Methods = [
                    new(Verbs.Cancel, typeof(CancelOperationHandler)),
                    new(Verbs.Wait,   typeof(WaitOperationHandler)),
                ];
            });
    }
}
