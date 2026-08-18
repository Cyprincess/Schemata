using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Features;
using Schemata.Transport.Http.Features;

namespace Schemata.Resource.Http.Features;

/// <summary>
///     Feature that sets up the MVC infrastructure for dynamically generated
///     <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instances
///     per <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>.
///     Shared HTTP plumbing (AIP-122 / AIP-154 wire-name rewrites) is supplied by
///     <see cref="SchemataTransportHttpFeature" />.
/// </summary>
[DependsOn<SchemataResourceFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataHttpResourceFeature : FeatureBase
{
    /// <summary>
    ///     Default endpoint priority for resource HTTP endpoints.
    /// </summary>
    public const int DefaultPriority = SchemataResourceFeature.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => services.AddSchemataHttpResources();

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) => app.UseSchemataHttpResources();
}
