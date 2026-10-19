using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Schemata.Core.Features;

public sealed class SchemataAuthorizationFeature : FeatureBase
{
    public const int DefaultPriority = SchemataAuthenticationFeature.DefaultPriority + 2_000_000;
    public override int Priority => DefaultPriority;

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
        app.UseAuthorization();
    }
}
