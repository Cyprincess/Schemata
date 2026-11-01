using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Transport.Http;
using Schemata.Transport.Http.Linking;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the shared HTTP transport services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Applies the Schemata JSON wire-name rewrites in <see cref="SchemataJsonTraits" /> to the
    ///     ambient <see cref="JsonSerializerOptions" />, to minimal-API JSON options and to MVC JSON
    ///     options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataJsonTraits(this IServiceCollection services) {
        services.PostConfigure<JsonSerializerOptions>(SchemataJsonTraits.Apply);
        services.PostConfigure<JsonOptions>(opts => SchemataJsonTraits.Apply(opts.SerializerOptions));
        services.PostConfigure<Microsoft.AspNetCore.Mvc.JsonOptions>(opts => SchemataJsonTraits.Apply(opts.JsonSerializerOptions));

        return services;
    }

    /// <summary>
    ///     Registers the single MVC result filter that writes the RFC 8288 <c>Link</c> header from
    ///     every registered <see cref="ILinkContributor" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataLinkHeaderShaping(this IServiceCollection services) {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>,
                                                                LinkHeaderMvcOptionsSetup>());

        return services;
    }

    /// <summary>
    ///     Registers an <see cref="ILinkContributor" /> so the shared HTTP response shaping collects
    ///     every contributor and serializes the links once.
    /// </summary>
    /// <typeparam name="TContributor">The contributor implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataLinkContributor<TContributor>(this IServiceCollection services)
        where TContributor : class, ILinkContributor {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ILinkContributor, TContributor>());

        return services;
    }
}
