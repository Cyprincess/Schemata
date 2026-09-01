using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Options;
using Schemata.Core.Building;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods wiring the MVC infrastructure that serves resources over HTTP.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the CRUD and custom-method controller feature providers, and the MVC conventions
    ///     that route the generated controllers. The CRUD provider doubles as the
    ///     <see cref="IActionDescriptorChangeProvider" /> that repopulates MVC once resources are known.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataHttpResources(this IServiceCollection services) {
        var provider = new ResourceControllerFeatureProvider();
        var method   = new ResourceMethodControllerFeatureProvider();
        services.AddSingleton(provider);
        services.AddSingleton(method);
        services.AddSingleton<IActionDescriptorChangeProvider>(provider);

        services.AddOptions<MvcOptions>()
                .Configure<ResourceRegistry, IOptions<SchemataResourceOptions>>((mvc, registry, opts) => {
                     mvc.Conventions.Add(new ResourceControllerConvention(registry, opts.Value.AuthenticationScheme));
                     mvc.Conventions.Add(new ResourceMethodControllerConvention(registry, opts.Value.AuthenticationScheme));
                 });

        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.FeatureProviders.Add(provider);
                     manager.FeatureProviders.Add(method);
                 });

        return services;
    }
}
