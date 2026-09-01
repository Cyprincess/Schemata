using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Internal;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Foundation.Handlers;
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
        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());

        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));
        services.TryAddScoped(typeof(ResourceMethodOperationHandler<,,>));

        services.AddHttpContextAccessor();
        services.AddDataProtection();

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateAdvisor<,>), typeof(AdviceApplyChildParent<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceApplyChildParent<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateSoftDeleted<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(AdviceUpdateFreshness<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(AdviceDeleteFreshness<>)));

        // Reverse-resolves an entity type from a resource name / collection segment.
        services.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();

        // The response ETag source for detail responses; overriding it swaps the weak-timestamp tag
        // for a domain-specific one.
        services.TryAddSingleton<IEntityTagProvider, DefaultEntityTagProvider>();

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
        var summary = resource.Summary!;

        AddStandardHandlers(services, entity, request, detail, summary);

        var createRequest  = typeof(CreateResourceRequest<,,>).MakeGenericType(entity, request, detail);
        var createResponse = typeof(CreateResultBase<>).MakeGenericType(detail);
        var updateRequest  = typeof(UpdateResourceRequest<,,>).MakeGenericType(entity, request, detail);
        var updateResponse = typeof(UpdateResultBase<>).MakeGenericType(detail);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateSanitizePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateValidationPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateSanitizePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateValidationPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));

        var listRequest  = typeof(ListResourceQueryRequest<,>).MakeGenericType(entity, summary);
        var listResponse = typeof(ListResultBase<>).MakeGenericType(summary);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(listRequest, listResponse), typeof(ResourceListResponsePipelineAdvisor<,>).MakeGenericType(entity, summary)));
        var getRequest  = typeof(GetResourceQueryRequest<,>).MakeGenericType(entity, detail);
        var getResponse = typeof(GetResultBase<>).MakeGenericType(detail);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(getRequest, getResponse), typeof(ResourceGetResponsePipelineAdvisor<,>).MakeGenericType(entity, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateResponsePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateResponsePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateIdempotencyPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateIdempotencyPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        var deleteRequest  = typeof(DeleteResourceRequest<,>).MakeGenericType(entity, detail);
        var deleteResponse = typeof(DeleteResultBase<>).MakeGenericType(detail);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(deleteRequest, deleteResponse), typeof(ResourceDeleteResponsePipelineAdvisor<,>).MakeGenericType(entity, detail)));

        var methods = entity.GetCustomAttributes<ResourceMethodAttribute>().ToList();
        if (resource.Methods is not null) {
            methods.AddRange(resource.Methods);
        }
        AddBuiltInMethods(resource, methods, entity, detail);

        foreach (var method in methods) {
            var descriptor = ResourceMethodHandlerHelper.Describe(entity, method.Handler);
            if (descriptor is null) {
                throw new InvalidOperationException(
                    $"Handler '{method.Handler.FullName}' for verb '{method.Verb}' on resource "
                    + $"'{entity.FullName}' must implement IRequestHandler<TRequest, TResponse>, "
                    + "where TRequest implements IRequest<TResponse> and IRequestPrincipal.");
            }

            var handlerInterface = ResourceMethodHandlerHelper.FindHandlerInterface(descriptor.Handler)!;
            services.TryAddScoped(handlerInterface, descriptor.Handler);

            var methodRequest  = descriptor.Request;
            var methodResponse = descriptor.Response;
            var envelope       = typeof(ResourceMethodRequest<,,>).MakeGenericType(entity, methodRequest, methodResponse);

            // TryAdd keeps one envelope handler per closure: a domain foundation that forwards its
            // own method command through the envelope registers its forwarder first.
            services.TryAddScoped(typeof(IRequestHandler<,>).MakeGenericType(envelope, methodResponse),
                                  typeof(ResourceMethodDispatchHandler<,,>).MakeGenericType(entity, methodRequest, methodResponse));
            services.TryAddEnumerable(ServiceDescriptor.Scoped(
                typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, methodResponse),
                typeof(ResourceMethodResponsePipelineAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse)));

            if (typeof(ICanonicalName).IsAssignableFrom(methodRequest)) {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(
                    typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, methodResponse),
                    typeof(ResourceMethodIdempotencyPipelineAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
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

    private static void AddStandardHandlers(
        IServiceCollection services,
        Type               entity,
        Type               request,
        Type               detail,
        Type               summary
    ) {
        AddHandler(
            services,
            typeof(IRequestHandler<,>).MakeGenericType(
                typeof(CreateResourceRequest<,,>).MakeGenericType(entity, request, detail),
                typeof(CreateResultBase<>).MakeGenericType(detail)),
            typeof(DefaultCreateResourceHandler<,,,>).MakeGenericType(entity, request, detail, summary));
        AddHandler(
            services,
            typeof(IRequestHandler<,>).MakeGenericType(
                typeof(GetResourceQueryRequest<,>).MakeGenericType(entity, detail),
                typeof(GetResultBase<>).MakeGenericType(detail)),
            typeof(DefaultGetResourceHandler<,,,>).MakeGenericType(entity, request, detail, summary));
        AddHandler(
            services,
            typeof(IRequestHandler<,>).MakeGenericType(
                typeof(ListResourceQueryRequest<,>).MakeGenericType(entity, summary),
                typeof(ListResultBase<>).MakeGenericType(summary)),
            typeof(DefaultListResourceHandler<,,,>).MakeGenericType(entity, request, detail, summary));
        AddHandler(
            services,
            typeof(IRequestHandler<,>).MakeGenericType(
                typeof(UpdateResourceRequest<,,>).MakeGenericType(entity, request, detail),
                typeof(UpdateResultBase<>).MakeGenericType(detail)),
            typeof(DefaultUpdateResourceHandler<,,,>).MakeGenericType(entity, request, detail, summary));
        AddHandler(
            services,
            typeof(IRequestHandler<,>).MakeGenericType(
                typeof(DeleteResourceRequest<,>).MakeGenericType(entity, detail),
                typeof(DeleteResultBase<>).MakeGenericType(detail)),
            typeof(DefaultDeleteResourceHandler<,,,>).MakeGenericType(entity, request, detail, summary));
    }

    private static void AddHandler(IServiceCollection services, Type service, Type implementation) {
        services.TryAdd(ServiceDescriptor.KeyedScoped(service, ResourceConstants.Handlers.Default, implementation));
        services.TryAdd(ServiceDescriptor.Scoped(service, sp =>
            sp.GetRequiredKeyedService(service, ResourceConstants.Handlers.Default)));
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
