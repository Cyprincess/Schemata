using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataRoutingFeature : FeatureBase
{
    public override int Priority => 150_000_000;

    public override void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) {
        services.AddRouting();
    }

    public override void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        app.UseRouting();
    }
}
