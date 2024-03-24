using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core;

public class SchemataBuilder(
    IServiceCollection  services,
    IConfiguration      configuration,
    IWebHostEnvironment environment,
    Configurators       configurators,
    SchemataOptions     options)
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    public readonly IConfiguration      Configuration = configuration;
    public readonly IWebHostEnvironment Environment   = environment;

    public readonly Configurators   Configurators = configurators;
    public readonly SchemataOptions Options       = options;

    public SchemataBuilder Configure<TOptions>(Action<TOptions> configure)
        where TOptions : class {
        return Configure(Microsoft.Extensions.Options.Options.DefaultName, configure);
    }

    public SchemataBuilder Configure<TOptions>(string name, Action<TOptions> configure)
        where TOptions : class {
        services.Configure(name, configure);

        return this;
    }

    public SchemataBuilder ConfigureServices(Action<IServiceCollection> action) {
        _actions.Add(action);

        return this;
    }

    public IServiceCollection Build() {
        foreach (var action in _actions) {
            action.Invoke(services);
        }

        services.AddSingleton(Options);

        return services;
    }
}
