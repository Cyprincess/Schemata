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
    ///     Commits one module selection per host: discovers the modules through
    ///     <typeparamref name="TProvider" />, publishes them on <paramref name="schemata" />, and
    ///     lets <typeparamref name="TRunner" /> (or the supplied <paramref name="runner" />
    ///     instance) contribute its registrations exactly once. The committed instance is the
    ///     <see cref="IModulesRunner" /> singleton, so the runtime lifecycle phases resolve the
    ///     same owner from the container. Repeating the call with the same runner and provider is
    ///     idempotent — discovery does not run again and no side effects repeat; a different
    ///     runner type, provider type, or runner instance is rejected before any discovery or
    ///     configuration, leaving the committed snapshot untouched.
    /// </summary>
    /// <typeparam name="TProvider">The module provider type.</typeparam>
    /// <typeparam name="TRunner">The module runner type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options bag the discovered modules are published on.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="environment">Host environment.</param>
    /// <param name="runner">An explicit runner instance; when null, one is created from the type.</param>
    /// <returns>The runner instance this call committed, or <see langword="null" /> when a previous
    ///     identical call already committed the selection.</returns>
    public static IModulesRunner? AddSchemataModules<TProvider, TRunner>(
        this IServiceCollection services,
        SchemataOptions         schemata,
        IConfiguration          configuration,
        IWebHostEnvironment     environment,
        TRunner?                runner = null
    )
        where TProvider : class, IModulesProvider
        where TRunner : class, IModulesRunner {
        var provider = typeof(TProvider);
        var type     = typeof(TRunner);

        // Selection precedes discovery and ConfigureServices side effects: an already-committed
        // host decides here, before any provider or runner can run.
        var existing = schemata.Get<ModuleSelection>(ModularConstants.SelectionKey);
        if (existing is not null) {
            if (existing.Runner != type || existing.Provider != provider) {
                throw new InvalidOperationException(
                    $"Schemata Modular supports one module runner and provider per host; "
                  + $"'{existing.Runner.FullName}' with '{existing.Provider.FullName}' is already selected.");
            }

            if (runner is not null && !ReferenceEquals(existing.RunnerInstance, runner)) {
                throw new InvalidOperationException(
                    $"Schemata Modular supports one module runner instance per host; a different "
                  + $"'{type.FullName}' instance was already selected.");
            }

            return null;
        }

        var providerInstance = Utilities.CreateInstance<IModulesProvider>(provider, schemata.CreateLogger(provider), configuration, environment, TimeProvider.System)
                             ?? throw new InvalidOperationException(
                                 $"Module provider type {provider.FullName} has no public constructor.");
        var modules = providerInstance.GetModules().ToList();
        schemata.SetModules(modules);

        var context = runner
                   ?? Utilities.CreateInstance<IModulesRunner>(type, schemata.CreateLogger(type), schemata, configuration, environment)
                   ?? throw new InvalidOperationException(
                       $"Module runner type {type.FullName} has no public constructor.");
        schemata.Set(ModularConstants.SelectionKey, new ModuleSelection(type, context, provider, modules));
        services.Replace(ServiceDescriptor.Singleton<IModulesRunner>(context));
        context.ConfigureServices(services, configuration, environment);

        return context;
    }
}
