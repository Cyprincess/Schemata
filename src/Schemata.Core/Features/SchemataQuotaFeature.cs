using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Core.Features;

/// <summary>
///     Enables ASP.NET Core rate limiting. On rejection, throws a
///     <see cref="QuotaExceededException" /> so the exception handler can produce a
///     structured <see cref="ErrorResponse" />. See <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </summary>
public sealed class SchemataQuotaFeature : FeatureBase
{
    /// <summary>
    ///     Default middleware priority for rate limiting.
    /// </summary>
    public const int DefaultPriority = SchemataRoutingFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<RateLimiterOptions>();
        services.AddRateLimiter(options => {
            configure(options);

            var rejected = options.OnRejected;
            options.OnRejected = async (ctx, ct) => {
                if (rejected is null) {
                    throw CreateQuotaExceededException(ctx.HttpContext);
                }

                await rejected(ctx, ct);

                if (ctx.HttpContext.Response.HasStarted) {
                    return;
                }

                throw CreateQuotaExceededException(ctx.HttpContext);
            };
        });
    }

    private static QuotaExceededException CreateQuotaExceededException(HttpContext context) {
        return new([new() { Subject = $"client:{context.Connection.RemoteIpAddress}", }]);
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseRateLimiter();
    }
}
