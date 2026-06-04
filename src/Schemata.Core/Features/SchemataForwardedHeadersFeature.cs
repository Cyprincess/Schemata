using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Features;

/// <summary>
///     Enables forwarded headers middleware so the app respects
///     <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> headers from reverse
///     proxies. Uses the deferred <see cref="ForwardedHeadersOptions" /> configurator
///     which defaults to clearing known networks and proxies for security.
/// </summary>
public sealed class SchemataForwardedHeadersFeature : FeatureBase
{
    public const int DefaultPriority = Orders.Base;

    public override int Priority => DefaultPriority;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseForwardedHeaders(new() {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });
    }
}
