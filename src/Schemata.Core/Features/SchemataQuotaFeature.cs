using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Features;

/// <summary>
///     Configures rate limiting middleware with structured error responses on rejection.
/// </summary>
public sealed class SchemataQuotaFeature : FeatureBase
{
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
                if (rejected is not null) {
                    await rejected(ctx, ct);

                    if (ctx.HttpContext.Response.HasStarted) {
                        return;
                    }
                }

                throw new QuotaExceededException(429, ErrorCodes.ResourceExhausted, SchemataResources.GetResourceString(SchemataResources.ST1011)) {
                    Details = [
                        new QuotaFailureDetail {
                            Violations = [
                                new() {
                                    Subject     = $"client:{ctx.HttpContext.Connection.RemoteIpAddress}",
                                    Description = SchemataResources.GetResourceString(SchemataResources.ST1011),
                                },
                            ],
                        },
                        new RequestInfoDetail { RequestId = ctx.HttpContext.TraceIdentifier },
                    ],
                };
            };
        });
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseRateLimiter();
    }
}
