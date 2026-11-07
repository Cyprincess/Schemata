using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Quota.Foundation.Features;

public sealed class SchemataQuotaAccountingFeature : FeatureBase
{
    public const int DefaultPriority = SchemataConstants.Orders.Extension + 150_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataQuotaAccounting();
    }
}
