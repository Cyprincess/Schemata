using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Modular;

public interface IModulesRunner
{
    public void ConfigureServices(
        IServiceCollection  services,
        IConfiguration      conf,
        IWebHostEnvironment env,
        IServiceProvider    provider);

    public void Configure(IApplicationBuilder app, IConfiguration configuration, IWebHostEnvironment environment);
}
