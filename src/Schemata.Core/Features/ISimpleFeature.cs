using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core.Features;

public interface ISimpleFeature : IFeature
{
    void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment);

    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);

    void ConfigureEndpoints(
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment);
}
