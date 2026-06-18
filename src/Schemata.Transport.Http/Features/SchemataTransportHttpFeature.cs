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
    /// <summary>First slot of the Extension priority range.</summary>
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
                    ex = new(500, ErrorCodes.Internal, SchemataResources.GetResourceString(SchemataResources.ST1013));
                }

                context.Response.StatusCode  = ex.Code;
                context.Response.ContentType = MediaTypeNames.Application.Json;

                var response = ex.CreateErrorResponse(context.TraceIdentifier);
                if (response is null) {
                    return;
                }

                await context.Response.WriteAsJsonAsync(response, options.Value, context.RequestAborted);
            });
        });
    }
}
