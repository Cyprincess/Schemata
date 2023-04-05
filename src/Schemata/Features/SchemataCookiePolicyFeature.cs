using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Features;

public class SchemataCookiePolicyFeature : FeatureBase
{
    public override int Priority => 140_000_000;

    private readonly Action<CookiePolicyOptions> _configure;

    public SchemataCookiePolicyFeature(Action<CookiePolicyOptions> configure) {
        _configure = configure;
    }

    public override void ConfigureServices(IServiceCollection services, IConfiguration conf, IWebHostEnvironment env) {
        services.AddCookiePolicy(_configure);
    }

    public override void Configure(IApplicationBuilder app, IConfiguration conf, IWebHostEnvironment env) {
        app.UseCookiePolicy();
    }
}
