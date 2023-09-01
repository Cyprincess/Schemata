using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Schemata.Features;

public class SchemataStaticFilesFeature : FeatureBase
{
    public override int Priority => 130_000_000;

    public override void Configure(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseStaticFiles();
    }
}
