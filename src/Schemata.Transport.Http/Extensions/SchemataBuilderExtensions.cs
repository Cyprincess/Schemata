using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Transport.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling shared HTTP transport services.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     <see cref="IServiceCollection" /> overload for use inside a feature's
    ///     <c>ConfigureServices</c>.
    /// </summary>
    /// <typeparam name="T">A type from the assembly to expose to MVC.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataApplicationPart<T>(this IServiceCollection services) {
        var configurator = new ApplicationPartConfigurator<T>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IApplicationPartConfigurator>(configurator));

        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     foreach (var existing in manager.ApplicationParts) {
                         if (existing.Name == configurator.PartName) {
                             return;
                         }
                     }

                     configurator.Configure(manager);
                 });

        return services;
    }
}
