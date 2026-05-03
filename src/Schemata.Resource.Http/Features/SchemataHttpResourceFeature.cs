using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;

namespace Schemata.Resource.Http.Features;

/// <summary>
///     Feature that sets up the MVC infrastructure for dynamically generated
///     <see cref="ResourceController{TEntity,TRequest,TDetail,TSummary}" /> instances
///     per <seealso href="https://google.aip.dev/127">AIP-127: HTTP and gRPC Transcoding</seealso>.
/// </summary>
[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataResourceFeature>]
public sealed class SchemataHttpResourceFeature : FeatureBase
{
    /// <summary>
    ///     Default priority for this feature, offset from <see cref="SchemataResourceFeature.DefaultPriority" />
    ///     to ensure resource definitions are registered before HTTP infrastructure is built.
    /// </summary>
    public const int DefaultPriority = SchemataResourceFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var provider = new ResourceControllerFeatureProvider();
        services.AddSingleton(provider);
        services.AddSingleton<IActionDescriptorChangeProvider>(provider);

        services.AddOptions<MvcOptions>()
                .Configure<IOptions<SchemataResourceOptions>>((mvc, opts) => {
                     mvc.Conventions.Add(new ResourceControllerConvention(opts.Value.AuthenticationScheme));
                 });

        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.FeatureProviders.Add(provider);
                 });
    }

    /// <inheritdoc />
    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var sp = app.ApplicationServices;

        var provider = sp.GetRequiredService<ResourceControllerFeatureProvider>();
        var options  = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        provider.Resources = options.Value.Resources;
        // Notify MVC that controllers have been added so action descriptors
        // are refreshed before the first request is served.
        provider.Commit();
    }
}
