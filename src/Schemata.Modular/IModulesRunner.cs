using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Modular;

public interface IModulesRunner
{
    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment);

    public void Configure(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);
}
