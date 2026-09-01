using Microsoft.Extensions.DependencyInjection;
using Schemata.Core.Building;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods publishing the resolved resource set to the MVC controller providers.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    ///     Hands the resolved resources and custom methods to the controller feature providers, then
    ///     signals MVC to repopulate. The CRUD provider is the change provider, so its commit
    ///     repopulates every registered feature provider, the custom-method one included.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseSchemataHttpResources(this IApplicationBuilder app) {
        var sp = app.ApplicationServices;

        var provider       = sp.GetRequiredService<ResourceControllerFeatureProvider>();
        var methodProvider = sp.GetRequiredService<ResourceMethodControllerFeatureProvider>();
        var registry       = sp.GetRequiredService<ResourceRegistry>();

        provider.Registry       = registry;
        methodProvider.Registry = registry;

        provider.Commit();

        return app;
    }
}
