using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Schemata.Abstractions.Errors;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Exceptions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Core.Json;
using Schemata.Core.State;

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
    ///     polymorphic type resolution — to the ambient serializer options, to minimal-API options,
    ///     and to MVC options whenever MVC is part of the application.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Applied last, so a caller can override any of it.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataJsonSerializer(
        this IServiceCollection             services,
        Action<JsonSerializerOptions> configure
    ) {
        services.Configure<JsonSerializerOptions>(Apply);

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => { Apply(options.SerializerOptions); });

        services.Configure<JsonOptions>(options => { Apply(options.JsonSerializerOptions); });

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
                    throw QuotaExceeded(ctx);
                }

                await rejected(ctx, ct);

                if (ctx.HttpContext.Response.HasStarted) {
                    return;
                }

                throw QuotaExceeded(ctx);
            };
        });

        return services;

        static QuotaExceededException QuotaExceeded(OnRejectedContext rejected) {
            var context = rejected.HttpContext;
            var hasSubject = rejected.Lease.TryGetMetadata(QuotaMetadata.Subject, out var supplied) && !string.IsNullOrWhiteSpace(supplied);
            var subject = hasSubject ? supplied : null;
            if (!hasSubject) {
                foreach (var identity in context.User.Identities) {
                    if (!identity.IsAuthenticated) continue;
                    subject = identity.FindFirst("sub")?.Value;
                    if (string.IsNullOrWhiteSpace(subject)) subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (string.IsNullOrWhiteSpace(subject)) subject = identity.Name;
                    if (!string.IsNullOrWhiteSpace(subject)) break;
                }
            }
            if (string.IsNullOrWhiteSpace(subject)) subject = $"client:{context.Connection.RemoteIpAddress}";
            var error = new QuotaExceededException([new() { Subject = subject }]);
            if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)) {
                error.Details!.Add(new RetryInfoDetail { RetryDelay = delay });
            }
            return error;
        }
    }

    /// <summary>
    ///     Registers the framework policy evaluator: the native evaluator under
    ///     <see cref="SchemataAuthenticationFeature.PolicyEvaluatorServiceKey" /> and, as the
    ///     public <see cref="IPolicyEvaluator" />, every registered
    ///     <see cref="IDecorator{T}" /> composed over it. The public composition is registered with
    ///     <c>Add</c>, so it stays the last registration and owns resolution whether
    ///     <c>AddAuthorization</c> ran before or after it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataPolicyEvaluator(this IServiceCollection services) {
        services.TryAddKeyedTransient<IPolicyEvaluator, PolicyEvaluator>(SchemataAuthenticationFeature.PolicyEvaluatorServiceKey);
        services.AddTransient<IPolicyEvaluator>(sp => Compose<IPolicyEvaluator>(sp, SchemataAuthenticationFeature.PolicyEvaluatorServiceKey));

        return services;
    }

    /// <summary>
    ///     Registers the framework controller factory: the factory <c>AddControllers</c> registered
    ///     under <see cref="SchemataControllersFeature.ControllerFactoryServiceKey" /> and, as the
    ///     public <see cref="IControllerFactory" />, every registered <see cref="IDecorator{T}" />
    ///     composed over it. Call after <c>AddControllers</c>; MVC's default factory type is
    ///     internal, so the registration it added is the only handle on it. Registering the seam
    ///     again leaves the existing keyed registration in place.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No unkeyed <see cref="IControllerFactory" /> is registered; <c>AddControllers</c> must be called first.
    /// </exception>
    public static IServiceCollection AddSchemataControllerFactory(this IServiceCollection services) {
        const string key = SchemataControllersFeature.ControllerFactoryServiceKey;

        if (services.Any(descriptor => descriptor.IsKeyedService
                                    && descriptor.ServiceType == typeof(IControllerFactory)
                                    && Equals(descriptor.ServiceKey, key))) {
            return services;
        }

        var index = services.Count - 1;
        while (index >= 0 && (services[index].IsKeyedService || services[index].ServiceType != typeof(IControllerFactory))) {
            index--;
        }
        if (index < 0) {
            throw new InvalidOperationException(
                "No IControllerFactory is registered; call AddControllers before the framework controller factory.");
        }

        var native = services[index];
        services[index] = native.ImplementationType is { } type
            ? new ServiceDescriptor(typeof(IControllerFactory), key, type, native.Lifetime)
            : native.ImplementationInstance is { } instance
                ? new ServiceDescriptor(typeof(IControllerFactory), key, instance)
                : new ServiceDescriptor(typeof(IControllerFactory), key,
                    (provider, _) => native.ImplementationFactory!(provider), native.Lifetime);

        services.Add(new ServiceDescriptor(typeof(IControllerFactory), sp => Compose<IControllerFactory>(sp, key), native.Lifetime));

        return services;
    }

    /// <summary>
    ///     Registers the single state branch provider over the collection, running every
    ///     <see cref="IStatePipelineValidator" /> once before the provider is created.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddStatePipelineProvider(this IServiceCollection services) {
        services.TryAddSingleton<IStatePipelineProvider>(sp => {
            foreach (var validator in sp.GetServices<IStatePipelineValidator>()) {
                validator.Validate();
            }

            return new DefaultStatePipelineProvider(sp);
        });

        return services;
    }

    private static T Compose<T>(IServiceProvider services, object key)
        where T : class {
        var service = services.GetRequiredKeyedService<T>(key);
        foreach (var decorator in services.GetServices<IDecorator<T>>()) {
            service = decorator.Decorate(service);
        }

        return service;
    }
}
