using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Features;

/// <summary>
///     Enables forwarded headers middleware so the app respects
///     <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> headers from reverse
///     proxies. The middleware reads the DI-configured
///     <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersOptions" /> built from the
///     deferred configurator, so consumer proxy and network selections apply.
/// </summary>
public sealed class SchemataForwardedHeadersFeature : FeatureBase
{
    /// <summary>
    ///     Default middleware priority for forwarded headers.
    /// </summary>
    public const int DefaultPriority = Orders.Base;

    public override int Priority => DefaultPriority;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseForwardedHeaders();
    }
}
