using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

[Information("Apps deployed in a reverse proxy configuration allow the proxy to handle connection security.",
    Level = LogLevel.Debug)]
public class SchemataHttpsFeature : FeatureBase
{
    public override int Priority => 120_000_000;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        if (environment.IsDevelopment()) {
            return;
        }

        app.UseHsts();
        app.UseHttpsRedirection();
    }
}
