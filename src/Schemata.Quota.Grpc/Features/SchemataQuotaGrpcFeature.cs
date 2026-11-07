using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Quota.Foundation.Features;
using Schemata.Transport.Grpc.Features;

namespace Schemata.Quota.Grpc.Features;

[DependsOn<SchemataQuotaAccountingFeature>]
[DependsOn<SchemataTransportGrpcFeature>]
public sealed class SchemataQuotaGrpcFeature : FeatureBase
{
    public const int DefaultPriority = 550_200_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataQuotaGrpc();

    public override void ConfigureEndpoints(
        IApplicationBuilder app,
        IEndpointRouteBuilder endpoints,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) => endpoints.MapSchemataQuotaGrpc();
}
