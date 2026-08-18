using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Resource.Http.Features;

namespace Schemata.Flow.Http.Features;

/// <summary>Registers Flow resources for the HTTP resource transport.</summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataFlowHttpFeature : FeatureBase
{
    /// <summary>Default <see cref="FeatureBase.Priority" /> for the Flow HTTP feature.</summary>
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<SchemataFlowHttpFeature>();
        services.AddSchemataFlowHttpResources(schemata);
    }
}
