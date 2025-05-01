using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Core.Features;

[DependsOn<SchemataJsonSerializerFeature>]
public sealed class SchemataExceptionHandlerFeature : FeatureBase
{
    public override int Priority => 100_010_000;

    public override void ConfigureApplication(
        IApplicationBuilder app,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        app.UseExceptionHandler(error => {
            error.Run(async context => {
                var options = context.RequestServices.GetRequiredService<IOptions<JsonSerializerOptions>>();

                var feature = context.Features.Get<IExceptionHandlerPathFeature>();

                if (feature?.Error is not HttpException http) {
                    context.Response.StatusCode  = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";

                    await context.Response.WriteAsJsonAsync(
                        new ErrorResponse {
                            ErrorDescription = "An error occurred.",
                        },
                        options.Value,
                        context.RequestAborted);

                    return;
                }

                context.Response.StatusCode = http.StatusCode;

                var response = new ErrorResponse {
                    ErrorDescription = http.Message,
                };

                if (http.Errors is { Count: > 0 }) {
                    response.Errors = http.Errors;
                } else {
                    response.Error = http.Error;
                }

                if (response.Errors is not null
                 || !string.IsNullOrWhiteSpace(response.Error)
                 || !string.IsNullOrWhiteSpace(response.ErrorDescription)) {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(response, options.Value, context.RequestAborted);
                }
            });
        });
    }
}
