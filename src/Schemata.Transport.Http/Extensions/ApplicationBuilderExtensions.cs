using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods installing the shared HTTP transport middlewares.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Installs the AIP-193 exception handler, mapping <see cref="SchemataException" /> subtypes
    ///     into structured error responses and every other exception into a generic 500.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemataExceptionHandler(this IApplicationBuilder app) {
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

        return app;
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
