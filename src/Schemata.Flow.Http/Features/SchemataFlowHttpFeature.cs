using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Flow.Foundation.Features;
using Schemata.Transport.Http.Features;

namespace Schemata.Flow.Http.Features;

/// <summary>
///     Exposes Flow process management endpoints over HTTP. The assembly containing
///     <c>ProcessController</c> is registered as an MVC
///     <see cref="Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPart" /> by
///     <c>SchemataBuilder.AddSchemataApplicationPart&lt;SchemataFlowHttpFeature&gt;()</c>
///     when <c>UseFlowHttp()</c> is invoked, which avoids the opt-in stripping
///     performed by <see cref="SchemataControllersFeature" /> for <c>Schemata.*</c>
///     assemblies. Shared HTTP plumbing (AIP-122 / AIP-154 wire-name rewrites) is
///     supplied by <see cref="SchemataTransportHttpFeature" />.
/// </summary>
[DependsOn<SchemataFlowFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataFlowHttpFeature : FeatureBase
{
    public const int DefaultPriority = SchemataFlowFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) { }
}
