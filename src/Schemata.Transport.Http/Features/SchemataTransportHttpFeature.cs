using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Transport.Http.Features;

/// <summary>
///     Shared HTTP transport stack: AIP-193 exception-handler middleware that maps
///     <see cref="SchemataException" /> subtypes into structured error responses, and
///     the Schemata JSON wire-name rewrites in <see cref="SchemataJsonTraits" />.
/// </summary>
[DependsOn<SchemataDeveloperExceptionPageFeature>]
[DependsOn<SchemataControllersFeature>]
[DependsOn<SchemataJsonSerializerFeature>]
public sealed class SchemataTransportHttpFeature : FeatureBase
{
    /// <summary>
    ///     Default priority for the shared HTTP transport feature.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 10_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.PostConfigure<JsonSerializerOptions>(SchemataJsonTraits.Apply);
        services.PostConfigure<JsonOptions>(opts => SchemataJsonTraits.Apply(opts.SerializerOptions));
        services.PostConfigure<Microsoft.AspNetCore.Mvc.JsonOptions>(opts => SchemataJsonTraits.Apply(opts.JsonSerializerOptions));
    }

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        app.UseExceptionHandler(error => {
            error.Run(async context => {
                var options = context.RequestServices
                                     .GetRequiredService<IOptions<JsonSerializerOptions>>();

                var feature = context.Features.Get<IExceptionHandlerPathFeature>();
                if (feature?.Error is null) {
                    return;
                }

                if (feature.Error is not SchemataException ex) {
                    ex = new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY));
                }

                context.Response.StatusCode  = ex.Code;
                context.Response.ContentType = MediaTypeNames.Application.Json;

                var locale   = ParseAcceptLanguage(context.Request.Headers.AcceptLanguage);
                var response = ex.CreateErrorResponse(context.TraceIdentifier, locale: locale);
                if (response is null) {
                    return;
                }

                await context.Response.WriteAsJsonAsync(response, options.Value, context.RequestAborted);
            });
        });
    }

    /// <summary>
    ///     Extracts the highest-quality language tag from an
    ///     <c>Accept-Language</c> header (e.g. <c>"zh-CN,en-US;q=0.9"</c> -> <c>"zh-CN"</c>).
    ///     Returns <see langword="null" /> when the header is empty so the central
    ///     <c>EnsureLocalizedMessage</c> helper skips localization.
    /// </summary>
    private static string? ParseAcceptLanguage(StringValues header) {
        foreach (var value in header) {
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            foreach (var segment in value.Split(',')) {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0) {
                    continue;
                }

                var semicolon = trimmed.IndexOf(';');
                var tag       = semicolon < 0 ? trimmed : trimmed[..semicolon].Trim();
                if (tag.Length == 0 || tag == "*") {
                    continue;
                }

                return tag;
            }
        }

        return null;
    }
}
