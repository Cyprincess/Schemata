using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core;

public class SchemataBuilder(
    IServiceCollection  services,
    IConfiguration      configuration,
    IWebHostEnvironment environment,
    Configurators       configurators,
    SchemataOptions     options)
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    internal readonly Configurators Configurators = configurators;

    public readonly IConfiguration      Configuration = configuration;
    public readonly IWebHostEnvironment Environment   = environment;

    public readonly SchemataOptions Options = options;

    public SchemataBuilder Configure<TOptions>(Action<TOptions> configure)
        where TOptions : class {
        Configurators.Set(configure);

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

        Configurators.Configure(services);

        services.AddSingleton(Options);

        return services;
    }
}
