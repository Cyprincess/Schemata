using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Features;

namespace Schemata.Resource.Http.Features;

[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataJsonSerializerFeature>]
[DependsOn<SchemataResourceFeature>]
[Information("Resource Service depends on Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
[Information("Resource Service depends on JsonSerializer feature, it will be added automatically.", Level = LogLevel.Debug)]
public sealed class SchemataHttpResourceFeature : FeatureBase
{
    public override int Priority => 360_100_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var provider = new ResourceControllerFeatureProvider();
        services.AddSingleton(provider);
        services.AddSingleton<IActionDescriptorChangeProvider>(provider);

        services.AddResourceJsonSerializerOptions();

        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.FeatureProviders.Add(provider);
                 });
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var sp = app.ApplicationServices;

        var provider = sp.GetRequiredService<ResourceControllerFeatureProvider>();
        var options  = sp.GetRequiredService<IOptions<SchemataResourceOptions>>();

        provider.Resources = options.Value.Resources;
        provider.Commit();
    }
}
