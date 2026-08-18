using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Modular;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods bootstrapping the modular system.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Discovers the modules through <typeparamref name="TProvider" />, publishes them on
    ///     <paramref name="schemata" />, then lets <typeparamref name="TRunner" /> contribute its own
    ///     registrations. A runner already in the collection wins, so a host can substitute its own.
    /// </summary>
    /// <typeparam name="TProvider">The module provider type.</typeparam>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag the discovered modules are published on.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataModules<TProvider, TRunner>(
        this IServiceCollection services,
        SchemataOptions         schemata,
        IConfiguration          configuration,
        IWebHostEnvironment     environment
    )
        where TProvider : class, IModulesProvider
        where TRunner : class, IModulesRunner {
        var provider = typeof(TProvider);
        var modules = Utilities.CreateInstance<IModulesProvider>(provider, schemata.CreateLogger(provider), configuration, environment, TimeProvider.System)!
                               .GetModules()
                               .ToList();
        schemata.SetModules(modules);

        if (services.Any(s => s.ServiceType == typeof(IModulesRunner))) {
            return services;
        }

        var runner  = typeof(TRunner);
        var context = Utilities.CreateInstance<IModulesRunner>(runner, schemata.CreateLogger(runner), schemata, configuration, environment)!;
        context.ConfigureServices(services, configuration, environment);
        services.TryAddSingleton<IModulesRunner>(_ => context);

        return services;
    }
}
