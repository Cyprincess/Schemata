using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Features;
using Schemata.Transport.Grpc;

namespace Schemata.Flow.Grpc.Features;

/// <summary>Registers Flow resources for the gRPC resource transport.</summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataFlowGrpcFeature : FeatureBase
{
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        RegisterHandlers(services);
        RegisterResources(new(schemata, services), GrpcResourceAttribute.Name);
        services.TryAddScoped<ProcessDefinitionService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTypeContributor, FlowProtoTypeContributor>());
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ProcessDefinitionService>();
    }

    private static void RegisterHandlers(IServiceCollection services) {
        services.TryAddScoped<StartProcessHandler>();
        services.TryAddScoped<CompleteActivityHandler>();
        services.TryAddScoped<CorrelateMessageHandler>();
        services.TryAddScoped<ThrowSignalHandler>();
        services.TryAddScoped<TerminateProcessHandler>();
    }

    private static void RegisterResources(SchemataResourceBuilder resources, string endpoint) {
        resources.Use<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>(
            [endpoint],
            resource => {
                resource.Operations = [Operations.Get, Operations.List];
                resource.Methods = [
                    new("start",     typeof(StartProcessHandler), ResourceMethodScope.Collection),
                    new("complete",  typeof(CompleteActivityHandler)),
                    new("correlate", typeof(CorrelateMessageHandler)),
                    new("signal",    typeof(ThrowSignalHandler), ResourceMethodScope.Collection),
                    new("terminate", typeof(TerminateProcessHandler)),
                ];
            });

        resources.Use<SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition>(
            [endpoint],
            resource => resource.Operations = [Operations.Get, Operations.List]);
    }
}
