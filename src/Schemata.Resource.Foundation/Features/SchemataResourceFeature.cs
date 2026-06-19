using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Expressions.Aip;
using Schemata.Resource.Foundation.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Features;

/// <summary>
///     Core feature that registers the resource advisor pipeline, and
///     auto-discovers resources decorated with <see cref="ResourceAttribute" />.
/// </summary>
[DependsOn<SchemataRoutingFeature>]
[DependsOn("Schemata.Mapping.Foundation.Features.SchemataMappingFeature`1")]
[DependsOn("Schemata.Security.Foundation.Features.SchemataSecurityFeature")]
public sealed class SchemataResourceFeature : FeatureBase
{
    /// <summary>
    ///     The default feature priority for resource service registration.
    /// </summary>
    public const int DefaultPriority = Orders.Extension + 90_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));
        services.TryAddScoped(typeof(ResourceMethodOperationHandler<,,>));
        services.AddAipExpressions();

        services.AddHttpContextAccessor();
        services.AddDataProtection();

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateSoftDeleted<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseReadMask<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListResponseAdvisor<>), typeof(AdviceListResponseReadMask<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseIdempotency<,>)));

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.IsDynamic) {
                continue;
            }

            if (assembly.GetName().Name?.StartsWith(nameof(Schemata) + ".", StringComparison.Ordinal) is true) {
                continue;
            }

            Type?[] types;
            try {
                types = assembly.GetExportedTypes();
            } catch (ReflectionTypeLoadException ex) {
                // A partial type-load failure still leaves usable exported types;
                // discover resources from the types that did load. Any other exception surfaces.
                types = ex.Types;
            }

            RegisterDiscoveredResources(services, types);
        }
    }

    /// <summary>
    ///     Registers every <see cref="ResourceAttribute" />-decorated type in <paramref name="types" />.
    ///     Null entries (types that failed to load) are skipped, so a partial assembly load still
    ///     registers the resources that resolved.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="types">Candidate types, some of which may be <see langword="null" />.</param>
    public static void RegisterDiscoveredResources(IServiceCollection services, IEnumerable<Type?> types) {
        foreach (var type in types) {
            if (type?.GetCustomAttribute<ResourceAttribute>() is { } attribute) {
                RegisterResource(services, attribute);
            }
        }
    }

    /// <summary>
    ///     Registers a single resource: resolves endpoints, adds the idempotency advisor
    ///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>, scans
    ///     AIP-136 custom methods declared via <see cref="ResourceMethodAttribute" />, and stores the
    ///     <see cref="ResourceAttribute" /> in <see cref="SchemataResourceOptions" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="resource">The <see cref="ResourceAttribute" /> describing the resource.</param>
    public static void RegisterResource(IServiceCollection services, ResourceAttribute resource) {
        resource.Endpoints ??= resource.Entity.GetCustomAttributes<ResourceEndpointAttributeBase>()
                                       .Select(a => a.Endpoint)
                                       .ToArray();

        var entity  = resource.Entity;
        var request = resource.Request!;
        var detail  = resource.Detail!;

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>).MakeGenericType(entity, request), typeof(AdviceCreateRequestIdempotency<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>).MakeGenericType(entity, request), typeof(AdviceUpdateRequestIdempotency<,,>).MakeGenericType(entity, request, detail)));

        var methods = entity.GetCustomAttributes<ResourceMethodAttribute>().ToList();
        if (resource.Methods is not null) {
            methods.AddRange(resource.Methods);
        }
        AddBuiltInMethods(resource, methods, entity, detail);

        // When the built-in purge method is active, register its restart-durable executor
        // and descriptor so a reloaded purge operation rebuilds from the persisted request.
        // Guarded by ISoftDelete because PurgeHandler/PurgeOperationHandler constrain TEntity.
        if (typeof(ISoftDelete).IsAssignableFrom(entity)) {
            var builtInPurge = typeof(PurgeHandler<>).MakeGenericType(entity);
            if (methods.Any(m => string.Equals(m.Verb, Verbs.Purge, StringComparison.Ordinal) && m.Handler == builtInPurge)) {
                var purgeKey = $"{Verbs.Purge}:{ResourceNameDescriptor.ForType(entity).Collection}";
                services.AddKeyedScoped(typeof(IOperationHandler<PurgeOperationArgs>), purgeKey,
                                        typeof(PurgeOperationHandler<>).MakeGenericType(entity));
                services.AddSingleton(new OperationDescriptor(purgeKey, Verbs.Purge, typeof(PurgeOperationArgs)));
            }
        }

        foreach (var method in methods) {
            var handlerInterface = FindResourceMethodHandlerInterface(method.Handler);
            if (handlerInterface is null) {
                throw new InvalidOperationException(
                    $"Handler '{method.Handler.FullName}' for verb '{method.Verb}' on resource "
                    + $"'{entity.FullName}' must implement IResourceMethodHandler<TEntity, TRequest, TResponse>.");
            }

            services.TryAddScoped(method.Handler);

            var arguments     = handlerInterface.GetGenericArguments();
            var methodRequest = arguments[1];
            var methodResponse = arguments[2];

            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>).MakeGenericType(entity, methodRequest), typeof(AdviceMethodRequestIdempotency<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse), typeof(AdviceMethodFreshness<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
        }

        services.Configure<SchemataResourceOptions>(options => {
            if (!options.Resources.TryGetValue(entity.TypeHandle, out var r)) {
                options.Resources[entity.TypeHandle] = resource;
            } else if (r.Endpoints is null || resource.Endpoints is null) {
                r.Endpoints = null;
            } else {
                foreach (var ep in resource.Endpoints) {
                    if (!r.Endpoints.Contains(ep)) {
                        r.Endpoints.Add(ep);
                    }
                }
            }

            if (methods.Count == 0) {
                return;
            }

            if (!options.Methods.TryGetValue(entity.TypeHandle, out var existing)) {
                options.Methods[entity.TypeHandle] = [..methods];
                return;
            }

            var byVerb = existing.ToDictionary(m => m.Verb, m => m);
            foreach (var m in methods) {
                byVerb[m.Verb] = m;
            }
            existing.Clear();
            existing.AddRange(byVerb.Values);
        });
    }

    private static Type? FindResourceMethodHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>)) {
                return iface;
            }
        }
        return null;
    }

    private static void AddBuiltInMethods(
        ResourceAttribute             resource,
        List<ResourceMethodAttribute> methods,
        Type                          entity,
        Type                          detail
    ) {
        if (!typeof(ISoftDelete).IsAssignableFrom(entity)) {
            return;
        }

        AddSoftDeleteMethod(
            methods,
            Verbs.Undelete,
            Operations.Undelete,
            typeof(UndeleteHandler<,>).MakeGenericType(entity, detail),
            resource.Operations);
        AddSoftDeleteMethod(
            methods,
            Verbs.Expunge,
            Operations.Expunge,
            typeof(ExpungeHandler<>).MakeGenericType(entity),
            resource.Operations);
        AddSoftDeleteMethod(
            methods,
            Verbs.Purge,
            Operations.Purge,
            typeof(PurgeHandler<>).MakeGenericType(entity),
            resource.Operations,
            ResourceMethodScope.Collection);
    }

    private static void AddSoftDeleteMethod(
        List<ResourceMethodAttribute> methods,
        string                        verb,
        Operations                    operation,
        Type                          handler,
        Operations[]?                 allowed,
        ResourceMethodScope           scope = ResourceMethodScope.Instance
    ) {
        if (allowed is not null && !allowed.Contains(operation)) {
            return;
        }

        if (methods.Any(m => string.Equals(m.Verb, verb, StringComparison.Ordinal))) {
            return;
        }

        methods.Add(new(verb, handler, scope));
    }
}
