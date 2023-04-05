using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataSessionFeature : FeatureBase
{
    public override int Priority => 180_000_000;

    private readonly Action<SessionOptions> _configure;

    public SchemataSessionFeature(Action<SessionOptions> configure) {
        _configure = configure;
    }

    public override void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) {
        services.AddSession(_configure);
    }

    public override void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        app.UseSession();
    }
}
