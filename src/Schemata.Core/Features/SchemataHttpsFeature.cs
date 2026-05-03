using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Schemata.Core.Features;

/// <summary>
///     Enforces HSTS and HTTPS redirection in non-Development environments. Skipped
///     in Development to allow plain-HTTP local testing.
/// </summary>
public sealed class SchemataHttpsFeature : FeatureBase
{
    /// <summary>
    ///     Priority for ordering the middleware registration in the application pipeline.
    /// </summary>
    public const int DefaultPriority = SchemataW3CLoggingFeature.DefaultPriority + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
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
