using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Grpc.Services;
using Schemata.Resource.Grpc.Features;

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
    ) => services.AddSchemataFlowGrpcResources(schemata);

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ProcessDefinitionService>();
    }
}
