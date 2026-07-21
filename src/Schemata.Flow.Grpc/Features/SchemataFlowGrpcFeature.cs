using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Grpc.Services;
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
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow gRPC feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        FlowResourceRegistration.RegisterHandlers(services);
        var resources = new SchemataResourceBuilder(schemata, services);
        resources.Use<SchemataProcess, SchemataProcess, SchemataProcess, SchemataProcess>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.ProcessOperations;
                resource.Methods    = FlowResourceRegistration.ProcessMethods;
            });
        resources.Use<SchemataProcessToken, SchemataProcessToken, SchemataProcessToken, SchemataProcessToken>(
            [GrpcResourceAttribute.Name],
            resource => {
                resource.Operations = FlowResourceRegistration.TokenOperations;
                resource.Methods    = FlowResourceRegistration.TokenMethods;
            });
        resources.Use<SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition, SchemataProcessTransition>(
            [GrpcResourceAttribute.Name],
            resource => resource.Operations = FlowResourceRegistration.TransitionOperations);
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
}
