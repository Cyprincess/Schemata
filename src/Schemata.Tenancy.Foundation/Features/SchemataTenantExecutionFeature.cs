using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Schemata.Core.Features;
using Schemata.Tenancy.Foundation.Middlewares;

namespace Schemata.Tenancy.Foundation.Features;

public sealed class SchemataTenantExecutionFeature : FeatureBase
{
    public override int Priority => SchemataAuthorizationFeature.DefaultPriority + 1_000_000;

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
        app.UseMiddleware<TenantExecutionMiddleware>();
    }
}
