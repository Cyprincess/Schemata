using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Core.Json;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering Schemata on <see cref="IServiceCollection" />:
///     <see cref="AddSchemata(IServiceCollection, IConfiguration, IWebHostEnvironment)" /> bootstraps the
///     host, and the remaining methods carry the built-in features' service registrations.
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
        services.TryAddSingleton(TimeProvider.System);

        schema?.Invoke(builder);
        configure?.Invoke(builder.Options);

        builder.Invoke(services);

        return services;
    }

    /// <summary>
    ///     Applies Schemata's JSON shape — snake_case names, string-number coercion, kebab-case enums,
    ///     polymorphic type resolution — to the ambient serializer options and to minimal-API options,
    ///     and to MVC options when <paramref name="mvc" /> is set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Applied last, so a caller can override any of it.</param>
    /// <param name="mvc">Whether MVC JSON options are present and should be configured too.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataJsonSerializer(
        this IServiceCollection       services,
        Action<JsonSerializerOptions> configure,
        bool                          mvc
    ) {
        services.Configure<JsonSerializerOptions>(Apply);

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => { Apply(options.SerializerOptions); });

        if (mvc) {
            services.Configure<JsonOptions>(options => { Apply(options.JsonSerializerOptions); });
        }

        return services;

        void Apply(JsonSerializerOptions options) {
            options.MaxDepth = 32;

            options.TypeInfoResolver = PolymorphicTypeResolver.Instance;

            options.DictionaryKeyPolicy    = JsonNamingPolicy.SnakeCaseLower;
            options.PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower;
            options.NumberHandling         = JsonNumberHandling.AllowReadingFromString;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
            options.Converters.Add(JsonStringNumberConverter.Instance);

            configure(options);
        }
    }

    /// <summary>
    ///     Registers MVC controllers and drops the <c>Schemata.*</c> assembly parts MVC discovered on
    ///     its own, so controllers arrive only through the application parts a feature declared.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">MVC options configuration.</param>
    /// <param name="build">Applied to the <see cref="IMvcBuilder" /> once controllers are registered.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataControllers(
        this IServiceCollection services,
        Action<MvcOptions>      configure,
        Action<IMvcBuilder>     build
    ) {
        var builder = services.AddControllers(configure);

        builder.ConfigureApplicationPartManager(manager => {
            var parts = manager.ApplicationParts.OfType<AssemblyPart>()
                               .Where(p => p.Name.StartsWith(nameof(Schemata) + "."))
                               .ToArray();

            foreach (var part in parts) {
                manager.ApplicationParts.Remove(part);
            }
        });

        build(builder);

        return services;
    }

    /// <summary>
    ///     Registers ASP.NET Core rate limiting and turns rejection into a
    ///     <see cref="QuotaExceededException" />, so a throttled request produces the same structured
    ///     error body as every other failure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Rate limiter configuration; its own <c>OnRejected</c> still runs first.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataRateLimiter(
        this IServiceCollection    services,
        Action<RateLimiterOptions> configure
    ) {
        services.AddRateLimiter(options => {
            configure(options);

            var rejected = options.OnRejected;
            options.OnRejected = async (ctx, ct) => {
                if (rejected is null) {
                    throw QuotaExceeded(ctx.HttpContext);
                }

                await rejected(ctx, ct);

                if (ctx.HttpContext.Response.HasStarted) {
                    return;
                }

                throw QuotaExceeded(ctx.HttpContext);
            };
        });

        return services;

        static QuotaExceededException QuotaExceeded(HttpContext context) {
            return new([new() { Subject = $"client:{context.Connection.RemoteIpAddress}", }]);
        }
    }
}
