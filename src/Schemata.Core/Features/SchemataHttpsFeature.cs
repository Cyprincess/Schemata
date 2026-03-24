using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Schemata.Core.Features;

/// <summary>
///     Configures HSTS and HTTPS redirection in non-Development environments.
/// </summary>
public sealed class SchemataHttpsFeature : FeatureBase
{
    public const int DefaultPriority = SchemataW3CLoggingFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        if (environment.IsDevelopment()) {
            return;
        }

        app.UseHsts();
        app.UseHttpsRedirection();
    }
}
