using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core.Features;

namespace Schemata.Core;

public sealed class SchemataBuilder(IConfiguration configuration, IWebHostEnvironment environment)
{
    public readonly Services        Services      = new();
    public readonly Configurators   Configurators = new();
    public readonly SchemataOptions Options       = new();

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

    public SchemataBuilder Configure<TOptions>(Action<TOptions> configure)
        where TOptions : class {
        Configurators.Set(configure);

        return this;
    }

    public SchemataBuilder Configure<T1, T2>(Action<T1, T2> configure) {
        Configurators.Set(configure);

        return this;
    }

    public SchemataBuilder ConfigureServices(Action<IServiceCollection> action) {
        Services.Add(action);

        return this;
    }

    public SchemataBuilder Invoke(IServiceCollection services) {
        Services.Invoke(services);

        var modules = Options.GetFeatures();
        if (modules is null) {
            return this;
        }

        var features = modules.Values.ToList();

        features.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var feature in features) {
            feature.ConfigureServices(services, Options, Configurators, Configuration, Environment);
        }

        Configurators.Invoke(services);

        return this;
    }
}
