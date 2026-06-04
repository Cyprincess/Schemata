using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Schemata.Core.Features;

/// <summary>
///     Enables the developer exception page only in the Development environment.
///     In non-Development environments this feature is a no-op.
/// </summary>
[Information("Developer Exception Page will only be enabled in Development environment.", Level = LogLevel.Debug)]
public sealed class SchemataDeveloperExceptionPageFeature : FeatureBase
{
    public const int DefaultPriority = SchemataForwardedHeadersFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        if (!environment.IsDevelopment()) {
            return;
        }

        app.UseDeveloperExceptionPage();
    }
}
