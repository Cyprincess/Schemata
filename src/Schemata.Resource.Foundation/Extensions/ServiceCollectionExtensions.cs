using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Scheduling.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering the AIP resource pipeline and individual resources.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the resource operation handlers and the advisor pipeline shared by every resource.
    ///     Individual resources are registered separately, through <see cref="SchemataResourceBuilder" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataResources(this IServiceCollection services) {
        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));
        services.TryAddScoped(typeof(ResourceMethodOperationHandler<,,>));

        services.AddHttpContextAccessor();
        services.AddDataProtection();

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(AdviceCreateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestSanitize<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(AdviceUpdateRequestValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateAdvisor<,>), typeof(AdviceApplyChildParent<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceApplyChildParent<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateSoftDeleted<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseParent<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListResponseAdvisor<>), typeof(AdviceListResponseParent<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceResponseAdvisor<,>), typeof(AdviceResponseIdempotency<,>)));

        // Reverse-resolves an entity type from a resource name / collection segment.
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();

        // The built-in AIP-165 purge runs as the restart-durable PurgeJob<TEntity>. One open-generic
        // registration resolves the job for any soft-deletable entity, and one resolver maps the
        // stable purge:{collection} key back to its closed-generic type so a reloaded purge operation
        // rebuilds after a restart.
        services.TryAddTransient(typeof(PurgeJob<>));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IScheduledJobKeyResolver, PurgeJobKeyResolver>());

        return services;
    }

    /// <summary>
    ///     Registers a single resource: resolves endpoints, adds the idempotency advisor
    ///     per <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>, scans
    ///     AIP-136 custom methods declared via <see cref="ResourceMethodAttribute" />, and stores the
    ///     <see cref="ResourceAttribute" /> in <paramref name="registry" />.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resource">The <see cref="ResourceAttribute" /> describing the resource.</param>
    /// <param name="registry">The registry owned by the calling <see cref="SchemataResourceBuilder" />.</param>
    /// <returns>The service collection for chaining.</returns>
    internal static IServiceCollection AddResource(
        this IServiceCollection services,
        ResourceAttribute       resource,
        ResourceRegistry        registry
    ) {
        EnsureAddressablePattern(resource.Entity);

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

        foreach (var method in methods) {
            var handlerInterface = FindResourceMethodHandlerInterface(method.Handler);
            if (handlerInterface is null) {
                throw new InvalidOperationException(
                    $"Handler '{method.Handler.FullName}' for verb '{method.Verb}' on resource "
                    + $"'{entity.FullName}' must implement IResourceMethodHandler<TEntity, TRequest, TResponse>.");
            }

            services.TryAddScoped(method.Handler);

            var arguments      = handlerInterface.GetGenericArguments();
            var methodRequest  = arguments[1];
            var methodResponse = arguments[2];

            if (typeof(ICanonicalName).IsAssignableFrom(methodRequest)) {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>).MakeGenericType(entity, methodRequest), typeof(AdviceMethodRequestIdempotency<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
                services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse), typeof(AdviceMethodFreshness<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
            }
        }

        registry.Add(resource, methods);

        return services;
    }

    /// <summary>
    ///     Rejects a resource whose <see cref="CanonicalNameAttribute" /> pattern cannot address a
    ///     single row; canonical patterns must identify individual resource rows before registration.
    /// </summary>
    private static void EnsureAddressablePattern(Type entity) {
        if (!typeof(ICanonicalName).IsAssignableFrom(entity)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType(entity);
        if (descriptor.IsAddressable) {
            return;
        }

        var found = descriptor.Pattern is null ? "no [CanonicalName]" : $"\"{descriptor.Pattern}\"";
        throw new InvalidOperationException(
            $"Resource '{entity.FullName}' must declare a [CanonicalName] pattern ending in a placeholder "
            + $"preceded by a collection literal, such as \"books/{{book}}\". Found {found}.");
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
