using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Grpc.Services;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Features;

namespace Schemata.Flow.Grpc.Features;

/// <summary>
///     Exposes Flow process management as a code-first gRPC <see cref="ProcessService" />.
///     Shared plumbing (<c>AddCodeFirstGrpc</c>, reflection, trait renames) is supplied
///     by <see cref="SchemataTransportGrpcFeature" />.
/// </summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataTransportGrpcFeature>]
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
        services.TryAddScoped<ProcessService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTypeContributor, FlowProtoTypeContributor>());
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ProcessService>();
    }
}
