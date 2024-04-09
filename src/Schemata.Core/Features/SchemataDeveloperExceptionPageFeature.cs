using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[Information("Developer Exception Page will only be enabled in Development environment.", Level = LogLevel.Debug)]
public sealed class SchemataDeveloperExceptionPageFeature : FeatureBase
{
    public override int Priority => 110_000_000;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        if (!environment.IsDevelopment()) {
            return;
        }

        app.UseDeveloperExceptionPage();
    }
}
