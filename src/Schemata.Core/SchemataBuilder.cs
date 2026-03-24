using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core.Features;

namespace Schemata.Core;

/// <summary>
///     Fluent builder for configuring and registering Schemata framework features and services.
/// </summary>
public sealed class SchemataBuilder
{
    /// <summary>
    ///     The configurator registry for deferred option configuration.
    /// </summary>
    public readonly Configurators Configurators = new();

    /// <summary>
    ///     The central options container shared across all Schemata features.
    /// </summary>
    public readonly SchemataOptions Options = new();

    /// <summary>
    ///     A staging service collection that accumulates services before they are flushed to the host container.
    /// </summary>
    public readonly IServiceCollection Services = new Services();

    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataBuilder" /> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    public SchemataBuilder(IConfiguration configuration, IWebHostEnvironment environment) {
        Configuration = configuration;
        Environment   = environment;
    }

    /// <summary>
    ///     Gets the application configuration.
    /// </summary>
    public IConfiguration Configuration { get; }

    /// <summary>
    ///     Gets the web host environment.
    /// </summary>
    public IWebHostEnvironment Environment { get; }

    /// <summary>
    ///     Registers a feature type with the framework.
    /// </summary>
    /// <typeparam name="T">The feature type to add.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder AddFeature<T>()
        where T : ISimpleFeature {
        Options.AddFeature<T>();

        return this;
    }

    /// <summary>
    ///     Checks whether a feature type has already been registered.
    /// </summary>
    /// <typeparam name="T">The feature type to check.</typeparam>
    /// <returns><see langword="true" /> if the feature is registered.</returns>
    public bool HasFeature<T>()
        where T : ISimpleFeature {
        return Options.HasFeature<T>();
    }

    /// <summary>
    ///     Replaces the logger factory used by the builder.
    /// </summary>
    /// <param name="factory">The new logger factory.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder ReplaceLoggerFactory(ILoggerFactory factory) {
        Options.ReplaceLoggerFactory(factory);

        return this;
    }

    /// <summary>
    ///     Creates a typed logger using the current logging factory.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    /// <returns>A logger instance.</returns>
    public ILogger<T> CreateLogger<T>() { return Options.CreateLogger<T>(); }

    /// <summary>
    ///     Creates a logger for the specified type.
    /// </summary>
    /// <param name="type">The type to create a logger for.</param>
    /// <returns>A logger instance.</returns>
    public object? CreateLogger(Type type) { return Options.CreateLogger(type); }

    /// <summary>
    ///     Registers a deferred configuration action for the specified options type.
    /// </summary>
    /// <typeparam name="TOptions">The options type to configure.</typeparam>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder Configure<TOptions>(Action<TOptions> configure)
        where TOptions : class {
        Configurators.Set(configure);

        return this;
    }

    /// <summary>
    ///     Registers a deferred configuration action with two parameters.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder Configure<T1, T2>(Action<T1, T2> configure) {
        Configurators.Set(configure);

        return this;
    }

    /// <summary>
    ///     Allows direct configuration of the staging service collection.
    /// </summary>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder ConfigureServices(Action<IServiceCollection> configure) {
        configure(Services);

        return this;
    }

    /// <summary>
    ///     Flushes all staged services and registered features into the target service collection.
    /// </summary>
    /// <param name="services">The host service collection to flush into.</param>
    /// <returns>This builder for chaining.</returns>
    public SchemataBuilder Invoke(IServiceCollection services) {
        foreach (var service in Services) {
            services.Add(service);
        }

        Services.Clear();

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
