using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Modular;

public interface IModulesRunner
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment);

    void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);

    void ConfigureEndpoints(
        IApplicationBuilder   app,
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment);
}
