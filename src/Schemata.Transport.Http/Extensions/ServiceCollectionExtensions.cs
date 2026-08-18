using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Schemata.Transport.Http;

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
}
