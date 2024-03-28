using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Schemata.Modular;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class EndpointBuilderExtensions
{
    public static IEndpointRouteBuilder UseModular(
        this IEndpointRouteBuilder endpoints,
        IApplicationBuilder        app,
        IModulesRunner             runner,
        IConfiguration             configuration,
        IWebHostEnvironment        environment) {
        runner.ConfigureEndpoints(app, endpoints, configuration, environment);

        return endpoints;
    }
}
