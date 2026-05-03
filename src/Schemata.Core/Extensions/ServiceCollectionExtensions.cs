using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering Schemata on
///     <see cref="IServiceCollection" />. Creates a <see cref="SchemataBuilder" />,
///     registers <see cref="SchemataOptions" /> and <see cref="SchemataStartup" />,
///     then invokes the builder to flush staged services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Bootstraps Schemata with default options and no callbacks.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemata(
        this IServiceCollection services,
        IConfiguration          configuration,
        IWebHostEnvironment     environment
    ) {
        return services.AddSchemata(configuration, environment, _ => { }, _ => { });
    }

    /// <summary>
    ///     Bootstraps Schemata and configures features via
    ///     <paramref name="schema" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="schema">Callback that configures features and services on the builder.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataBuilder>? schema
    ) {
        return services.AddSchemata(configuration, environment, schema, _ => { });
    }

    /// <summary>
    ///     Bootstraps Schemata and mutates <see cref="SchemataOptions" /> via
    ///     <paramref name="configure" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="configure">Callback that mutates <see cref="SchemataOptions" /> directly.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataOptions>? configure
    ) {
        return services.AddSchemata(configuration, environment, _ => { }, configure);
    }

    /// <summary>
    ///     Bootstraps Schemata: creates the builder, registers the startup filter
    ///     and options singleton, applies user callbacks, then invokes the
    ///     builder.
    /// </summary>
    /// <param name="services">Host service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="schema">Callback that configures features and services on the builder.</param>
    /// <param name="configure">Callback that mutates <see cref="SchemataOptions" /> directly.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemata(
        this IServiceCollection  services,
        IConfiguration           configuration,
        IWebHostEnvironment      environment,
        Action<SchemataBuilder>? schema,
        Action<SchemataOptions>? configure
    ) {
        var builder = new SchemataBuilder(configuration, environment);

        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, SchemataStartup>(_ => SchemataStartup.Create(configuration, environment)));

        services.TryAddSingleton(builder.Options);

        schema?.Invoke(builder);
        configure?.Invoke(builder.Options);

        builder.Invoke(services);

        return services;
    }
}
