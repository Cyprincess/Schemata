using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Features;

/// <summary>
///     Configures global exception handling, converting <see cref="SchemataException" /> to structured JSON error
///     responses.
/// </summary>
[DependsOn<SchemataJsonSerializerFeature>]
public sealed class SchemataExceptionHandlerFeature : FeatureBase
{
    public const int DefaultPriority = SchemataDeveloperExceptionPageFeature.DefaultPriority + 10_000_000;

    public override int Priority => DefaultPriority;

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

                if (feature?.Error is not SchemataException http) {
                    context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = MediaTypeNames.Application.Json;

                    await context.Response.WriteAsJsonAsync(new ErrorResponse {
                        Error = new() {
                            Code    = ErrorCodes.Internal,
                            Message = SchemataResources.GetResourceString(SchemataResources.ST1012),
                            Details = [new RequestInfoDetail {
                                RequestId = context.TraceIdentifier,
                            }],
                        },
                    }, options.Value, context.RequestAborted);

                    return;
                }

                context.Response.StatusCode = http.Status;

                var response = http.CreateErrorResponse();
                if (response is null) {
                    return;
                }

                context.Response.ContentType = MediaTypeNames.Application.Json;

                await context.Response.WriteAsJsonAsync(response, options.Value, context.RequestAborted);
            });
        });
    }
}
