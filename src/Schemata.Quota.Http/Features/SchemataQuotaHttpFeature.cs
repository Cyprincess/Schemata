using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Quota.Foundation.Features;
using Schemata.Transport.Http.Features;

namespace Schemata.Quota.Http.Features;

[DependsOn<SchemataQuotaAccountingFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataQuotaHttpFeature : FeatureBase
{
    public const int DefaultPriority = 550_100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataQuotaHttp();
}
