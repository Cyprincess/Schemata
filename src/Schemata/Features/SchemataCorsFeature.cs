using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataCorsFeature : FeatureBase
{
    public override int Priority => 160_000_000;

    private readonly Action<CorsOptions> _configure;

    public SchemataCorsFeature(Action<CorsOptions> configure) {
        _configure = configure;
    }

    public override void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) {
        services.AddCors(_configure);
    }

    public override void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        app.UseCors();
    }
}
