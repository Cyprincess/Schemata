using Grpc.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Grpc.Features;

/// <summary>
///     Shared gRPC transport stack: code-first protobuf-net serialization, the exception-mapping
///     interceptor, gRPC server reflection, and the protobuf runtime type model configured from the
///     registered proto type contributors.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
public sealed class SchemataTransportGrpcFeature : FeatureBase
{
    /// <summary>
    ///     Default priority for the shared gRPC transport feature.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 20_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataGrpcTransport();

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => app.UseSchemataProtoModel();

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<ReflectionServiceImpl>();
        endpoints.MapGrpcService<ReflectionV1ServiceImpl>();
    }
}
