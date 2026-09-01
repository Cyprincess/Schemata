using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Features;

/// <summary>
///     Core feature that registers the resource advisor pipeline. Individual resources are registered
///     explicitly through <c>SchemataResourceBuilder.AddResource&lt;TEntity&gt;()</c> or
///     <c>Use&lt;TEntity,TRequest,TDetail,TSummary&gt;()</c>.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
public sealed class SchemataResourceFeature : FeatureBase
{
    /// <summary>
    ///     The default feature priority for resource service registration.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 100_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataResources(schemata);
}
