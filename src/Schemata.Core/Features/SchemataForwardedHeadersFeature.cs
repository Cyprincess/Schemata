using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Schemata.Abstractions;

namespace Schemata.Core.Features;

/// <summary>
///     Configures forwarded headers middleware for proxy scenarios.
/// </summary>
public sealed class SchemataForwardedHeadersFeature : FeatureBase
{
    public const int DefaultPriority = SchemataConstants.Orders.Base;

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
