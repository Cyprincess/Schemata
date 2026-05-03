using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Core.Features;

namespace Schemata.Core;

/// <summary>
///     Fluent builder for assembling a Schemata application. Holds a staging
///     <see cref="IServiceCollection" />, deferred <see cref="Configurators" />,
///     and the shared <see cref="SchemataOptions" /> container. Call
///     <see cref="Invoke" /> to flush staged services and run feature
///     configuration.
/// </summary>
public sealed class SchemataBuilder
{
    /// <summary>
    ///     Registry of deferred configuration actions applied when
    ///     <see cref="Invoke" /> is called.
    /// </summary>
    public readonly Configurators Configurators = new();

    /// <summary>
    ///     Central key-value store shared by all features for named options and
    ///     feature registration.
    /// </summary>
    public readonly SchemataOptions Options = new();

    /// <summary>
    ///     In-memory service collection that accumulates registrations before
    ///     being flushed to the host container.
    /// </summary>
    public readonly IServiceCollection Services = new Services();

    /// <summary>
    ///     Initializes a new <see cref="SchemataBuilder" /> with the application
    ///     configuration and hosting environment.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="environment">The web host environment.</param>
    public SchemataBuilder(IConfiguration configuration, IWebHostEnvironment environment) {
        Configuration = configuration;
        Environment   = environment;
    }

    /// <summary>
    ///     The application configuration.
    /// </summary>
    public IConfiguration Configuration { get; }

    /// <summary>
    ///     The web host environment.
    /// </summary>
    public IWebHostEnvironment Environment { get; }

    /// <summary>
    ///     Registers a feature type for the next <see cref="Invoke" /> cycle.
    /// </summary>
    /// <typeparam name="T">A concrete <see cref="ISimpleFeature" /> implementation.</typeparam>
    /// <returns>The builder for chaining.</returns>
    public SchemataBuilder AddFeature<T>()
        where T : ISimpleFeature {
        Options.AddFeature<T>();

        return this;
    }

    /// <summary>
    ///     Returns <see langword="true" /> if the given feature type was already
    ///     registered.
    /// </summary>
    /// <typeparam name="T">The feature type to check.</typeparam>
    /// <returns><see langword="true" /> when the feature is registered.</returns>
    public bool HasFeature<T>()
        where T : ISimpleFeature {
        return Options.HasFeature<T>();
    }

    /// <summary>
    ///     Swaps the logger factory used by the builder and all features that log
    ///     through <see cref="SchemataOptions" />.
    /// </summary>
    /// <param name="factory">The replacement <see cref="ILoggerFactory" />.</param>
    /// <returns>The builder for chaining.</returns>
    public SchemataBuilder ReplaceLoggerFactory(ILoggerFactory factory) {
        Options.ReplaceLoggerFactory(factory);

        return this;
    }

    /// <summary>
    ///     Creates a typed <see cref="ILogger{T}" /> from the current
    ///     <see cref="SchemataOptions.Logging" /> factory. Convenience forwarder
    ///     to <see cref="SchemataOptions.CreateLogger{T}" />.
    /// </summary>
    /// <typeparam name="T">The category type for the logger.</typeparam>
    /// <returns>A <see cref="ILogger{T}" /> instance.</returns>
    public ILogger<T> CreateLogger<T>() { return Options.CreateLogger<T>(); }

    /// <summary>
    ///     Creates a typed <c>ILogger</c> via <c>Logger&lt;T&gt;</c> reflection
    ///     using the current <see cref="SchemataOptions.Logging" /> factory.
    ///     Convenience forwarder to
    ///     <see cref="SchemataOptions.CreateLogger(Type)" />.
    /// </summary>
    /// <param name="type">The category type for the logger.</param>
    /// <returns>
    ///     An <see cref="ILogger" /> instance, or <see langword="null" /> if creation fails.
    /// </returns>
    public object? CreateLogger(Type type) { return Options.CreateLogger(type); }

    /// <summary>
    ///     Queues a configuration delegate that will be applied to a named
    ///     <typeparamref name="TOptions" /> instance during <see cref="Invoke" />.
    /// </summary>
    /// <typeparam name="TOptions">The options type to configure.</typeparam>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public SchemataBuilder Configure<TOptions>(Action<TOptions> configure)
        where TOptions : class {
        Configurators.Set(configure);

        return this;
    }

    /// <summary>
    ///     Queues a two-parameter configuration delegate executed during
    ///     <see cref="Invoke" /> (e.g. <see cref="AuthenticationBuilder" />
    ///     callbacks).
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public SchemataBuilder Configure<T1, T2>(Action<T1, T2> configure) {
        Configurators.Set(configure);

        return this;
    }

    /// <summary>
    ///     Directly mutates the staging <see cref="Services" /> collection before
    ///     the flush.
    /// </summary>
    /// <param name="configure">A delegate that adds to the staging collection.</param>
    /// <returns>The builder for chaining.</returns>
    public SchemataBuilder ConfigureServices(Action<IServiceCollection> configure) {
        configure(Services);

        return this;
    }

    /// <summary>
    ///     Flushes all staged services into <paramref name="services" />, then
    ///     invokes <see cref="ISimpleFeature.ConfigureServices" /> for every
    ///     registered feature in priority order.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <returns>The builder for chaining.</returns>
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
