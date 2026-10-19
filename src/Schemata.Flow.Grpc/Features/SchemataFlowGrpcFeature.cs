using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Grpc.Services;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Resource.Grpc.Features;

namespace Schemata.Flow.Grpc.Features;

/// <summary>Registers Flow resources for the gRPC resource transport.</summary>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TTransition">The persisted transition entity type registered by the host.</typeparam>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataGrpcResourceFeature>]
public sealed class SchemataFlowGrpcFeature<TProcess, TToken, TTransition> : FeatureBase
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TTransition : SchemataProcessTransition
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
    ) => services.AddSchemataFlowGrpcResources<TProcess, TToken, TTransition>(schemata);

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ProcessDefinitionService>();
    }
}
