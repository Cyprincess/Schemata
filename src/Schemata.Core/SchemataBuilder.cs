using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core.Features;

namespace Schemata.Core;

public class SchemataBuilder(
    IServiceCollection  services,
    IConfiguration      configuration,
    IWebHostEnvironment environment,
    Configurators       configurators,
    SchemataOptions     options)
{
    private readonly List<Action<IServiceCollection>> _actions = [];

    private Configurators   Configurators => configurators;
    private SchemataOptions Options       => options;

    public IConfiguration      Configuration => configuration;
    public IWebHostEnvironment Environment   => environment;

    public SchemataBuilder AddFeature<T>()
        where T : ISimpleFeature {
        Options.AddFeature<T>();

        return this;
    }

    public bool HasFeature<T>()
        where T : ISimpleFeature {
        return Options.HasFeature<T>();
    }

    public SchemataBuilder ReplaceLoggerFactory(ILoggerFactory factory) {
        Options.ReplaceLoggerFactory(factory);

        return this;
    }

    public ILogger<T> CreateLogger<T>() {
        return Options.CreateLogger<T>();
    }

    public object? CreateLogger(Type type) {
        return Options.CreateLogger(type);
    }

    public Configurators GetConfigurators() {
        return Configurators;
    }

    public SchemataOptions GetOptions() {
        return Options;
    }

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
