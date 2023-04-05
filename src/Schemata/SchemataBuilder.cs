using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata;

public class SchemataBuilder
{
    private readonly IServiceCollection _services;

    public readonly IConfiguration      Configuration;
    public readonly IWebHostEnvironment Environment;

    public SchemataBuilder(IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment) {
        _services     = services;
        Configuration = configuration;
        Environment   = environment;
    }

    public SchemataBuilder Configure<TOptions>(Action<TOptions>? configure)
        where TOptions : class {
        return Configure(Options.DefaultName, configure);
    }

    public SchemataBuilder Configure<TOptions>(string name, Action<TOptions>? configure)
        where TOptions : class {
        _services.Configure(name, configure);

        return this;
    }

    public SchemataBuilder ConfigureServices(Action<IServiceCollection> services) {
        services.Invoke(_services);

        return this;
    }

    public IServiceCollection Build() {
        return _services;
    }
}
