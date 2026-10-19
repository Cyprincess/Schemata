using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Schemata.Core.Features;
using Schemata.Tenancy.Foundation.Middlewares;
using Schemata.Tenancy.Skeleton.Entities;

namespace Schemata.Tenancy.Foundation.Features;

public sealed class SchemataTenantPrincipalFeature<TTenant> : FeatureBase where TTenant : SchemataTenant
{
    public override int Priority => SchemataAuthenticationFeature.DefaultPriority + 1_000_000;

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment) {
        app.UseMiddleware<SchemataTenantPrincipalMiddleware<TTenant>>();
    }
}
