using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Http.Features;

/// <summary>
///     Shared HTTP transport stack: the AIP-193 exception-handler middleware and the Schemata JSON
///     wire-name rewrites.
/// </summary>
[DependsOn<SchemataDeveloperExceptionPageFeature>]
[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataJsonSerializerFeature>]
public sealed class SchemataTransportHttpFeature : FeatureBase
{
    /// <summary>
    ///     Default priority for the shared HTTP transport feature.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataJsonTraits();

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => app.UseSchemataRequestCulture()
             .UseSchemataExceptionHandler();
}
