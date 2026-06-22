using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Insight.Foundation.Features;
using Schemata.Transport.Grpc.Features;

namespace Schemata.Insight.Grpc.Features;

/// <summary>
///     Exposes the Insight query over gRPC by registering the code-first service and its method
///     provider. Shared gRPC plumbing (code-first serialization, exception mapping, reflection) comes
///     from <see cref="SchemataTransportGrpcFeature" />.
/// </summary>
[DependsOn<SchemataInsightFeature>]
[DependsOn<SchemataTransportGrpcFeature>]
public sealed class SchemataInsightGrpcFeature : FeatureBase
{
    /// <summary>The default endpoint priority for the Insight gRPC endpoint.</summary>
    public const int DefaultPriority = SchemataInsightFeature.DefaultPriority + 200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped<InsightGrpcService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IServiceMethodProvider<InsightGrpcService>, InsightServiceMethodProvider>());
    }

    public override void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment
    ) {
        endpoints.MapGrpcService<InsightGrpcService>();
    }
}
