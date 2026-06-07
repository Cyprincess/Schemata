using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Transport.Http;
using Schemata.Transport.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     <c>UseHttpTransport</c> and <c>AddSchemataApplicationPart&lt;T&gt;</c> extensions.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Enables <see cref="SchemataTransportHttpFeature" />.</summary>
    public static SchemataBuilder UseHttpTransport(this SchemataBuilder builder) {
        builder.AddFeature<SchemataTransportHttpFeature>();

        return builder;
    }

    /// <summary>
    ///     Registers a <see cref="SchemataExtensionPart{T}" /> on MVC and exposes the
    ///     matching <see cref="IApplicationPartConfigurator" /> through DI. Idempotent.
    /// </summary>
    public static SchemataBuilder AddSchemataApplicationPart<T>(this SchemataBuilder builder) {
        builder.Services.AddSchemataApplicationPart<T>();
        return builder;
    }

    /// <summary>
    ///     <see cref="IServiceCollection" /> overload for use inside a feature's
    ///     <c>ConfigureServices</c>.
    /// </summary>
    public static IServiceCollection AddSchemataApplicationPart<T>(this IServiceCollection services) {
        var configurator = new ApplicationPartConfigurator<T>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IApplicationPartConfigurator>(configurator));

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
