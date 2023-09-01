using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata;

public class SchemataBuilder
{
    private readonly IServiceCollection _services;

    public readonly IConfiguration      Configuration;
    public readonly IWebHostEnvironment Environment;
    public readonly SchemataOptions     Options;

    public readonly Configurators Configurators;

    public SchemataBuilder(
        IServiceCollection  services,
        IConfiguration      configuration,
        IWebHostEnvironment environment,
        SchemataOptions     options) {
        _services = services;

        Configuration = configuration;
        Environment   = environment;
        Options       = options;

        Configurators = new Configurators();
    }

    public SchemataBuilder Configure<TOptions>(Action<TOptions>? configure)
        where TOptions : class {
        return Configure(Microsoft.Extensions.Options.Options.DefaultName, configure);
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
