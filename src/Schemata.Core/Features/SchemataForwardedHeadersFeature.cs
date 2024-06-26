using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Schemata.Core.Features;

public sealed class SchemataForwardedHeadersFeature : FeatureBase
{
    public override int Priority => 111_000_000;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseForwardedHeaders();
    }
}
