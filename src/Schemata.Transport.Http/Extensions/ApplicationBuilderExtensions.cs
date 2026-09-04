using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Globalization;
using Schemata.Transport.Http.Middleware;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods installing the shared HTTP transport middlewares.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Installs <see cref="RequestCultureMiddleware" /> which flows the
    ///     <c>Accept-Language</c> preference into the request culture.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemataRequestCulture(this IApplicationBuilder app) {
        return app.UseMiddleware<RequestCultureMiddleware>();
    }

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
                    ex = new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.INTERNAL));
                }

                context.Response.StatusCode  = ex.Code;
                context.Response.ContentType = MediaTypeNames.Application.Json;

                var locale   = AcceptLanguageParser.Parse(context.Request.Headers.AcceptLanguage)?.Name;
                var response = ex.CreateErrorResponse(context.TraceIdentifier, locale: locale);
                if (response is null) {
                    return;
                }

                await context.Response.WriteAsJsonAsync(response, options.Value, context.RequestAborted);
            });
        });

        return app;
    }
}
