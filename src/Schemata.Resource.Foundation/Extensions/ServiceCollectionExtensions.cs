using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Building;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Messaging.Skeleton.Advisors;
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
        services.AddInProcessRequestDispatcher();

        services.TryAddScoped(typeof(ResourceOperationHandler<,,,>));
        services.TryAddScoped(typeof(ResourceMethodOperationHandler<,,>));

        services.AddHttpContextAccessor();
        services.AddDataProtection();

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
    ///     Registers the shared resource pipeline over the registry carried by <paramref name="schemata" />,
    ///     attaching the per-resource wiring this package supplies. Resources and security activations
    ///     recorded before this call are replayed through the wiring.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="schemata">The Schemata options carrying the shared resource registry.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataResources(this IServiceCollection services, SchemataOptions schemata) {
        AddSchemataResources(services);

        var registry = ResourceRegistry.GetOrAdd(schemata, services);
        AttachWiring(services, registry);
        return services;
    }

    private static void AttachWiring(IServiceCollection services, ResourceRegistry registry) {
        registry.Attach(services, new(
            PrepareMethods,
            (collection, resource) => InstallResource(collection, resource, registry),
            (collection, resource, method) => InstallMethod(collection, resource, method, registry),
            (resource, method) => {
                var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler)!;
                return (descriptor.Request, descriptor.Response);
            },
            ResourceAuthorizationRegistration.RegisterAuthentication,
            ResourceAuthorizationRegistration.RegisterAuthorization,
            collection => ApplyStageChoices(collection, registry)
        ));
    }


    /// <summary>
    ///     Applies the registry's stage choices to descriptors already in the collection: prunes the
    ///     validation stages and, when freshness is excluded, the freshness check advisors and the
    ///     default response-ETag provider (a host-supplied <see cref="IEntityTagProvider" /> is left
    ///     alone). Stages excluded before a resource registers are never installed for it.
    /// </summary>
    private static void ApplyStageChoices(IServiceCollection services, ResourceRegistry registry) {
        if (!registry.CreateValidation) {
            RemoveKeyedAdvisors(services, typeof(ResourceCreateValidationPipelineAdvisor<,,>));
        }

        if (!registry.UpdateValidation) {
            RemoveKeyedAdvisors(services, typeof(ResourceUpdateValidationPipelineAdvisor<,,>));
        }

        if (registry.Freshness) {
            return;
        }

        RemoveAdvisors(services, typeof(AdviceUpdateFreshness<,>));
        RemoveAdvisors(services, typeof(AdviceDeleteFreshness<>));
        RemoveAdvisors(services, typeof(AdviceMethodFreshness<,,>));
        for (var i = services.Count - 1; i >= 0; i--) {
            if (services[i].ServiceType == typeof(IEntityTagProvider)
             && !services[i].IsKeyedService
             && services[i].ImplementationType == typeof(DefaultEntityTagProvider)) {
                services.RemoveAt(i);
            }
        }
    }

    private static void RemoveAdvisors(IServiceCollection services, Type implementationDefinition) {
        for (var i = services.Count - 1; i >= 0; i--) {
            if (!services[i].IsKeyedService
             && services[i].ImplementationType is { IsGenericType: true } type
             && type.GetGenericTypeDefinition() == implementationDefinition) {
                services.RemoveAt(i);
            }
        }
    }

    private static void RemoveKeyedAdvisors(IServiceCollection services, Type implementationDefinition) {
        for (var i = services.Count - 1; i >= 0; i--) {
            var descriptor = services[i];
            if (descriptor.IsKeyedService
             && descriptor.ServiceKey is string key
             && key == RequestPipelineStages.Validation
             && descriptor.KeyedImplementationType is { IsGenericType: true } type
             && type.GetGenericTypeDefinition() == implementationDefinition) {
                services.RemoveAt(i);
            }
        }
    }

    internal static IServiceCollection AddResource(this IServiceCollection services, ResourceAttribute resource, ResourceRegistry registry) {
        AddSchemataResources(services);
        AttachWiring(services, registry);
        var methods = resource.Entity.GetCustomAttributes<ResourceMethodAttribute>().ToList();
        if (resource.Methods is not null) methods.AddRange(resource.Methods);
        registry.Register(services, resource, methods);
        return services;
    }

    private static void InstallResource(IServiceCollection services, ResourceRegistration resource, ResourceRegistry registry) {

        var entity  = resource.Entity;
        var request = resource.Request;
        var detail  = resource.Detail;
        var summary = resource.Summary;

        AddStandardHandlers(services, entity, request, detail, summary);

        var createRequest  = typeof(CreateResourceRequest<,,>).MakeGenericType(entity, request, detail);
        var createResponse = typeof(CreateResultBase<>).MakeGenericType(detail);
        var updateRequest  = typeof(UpdateResourceRequest<,,>).MakeGenericType(entity, request, detail);
        var updateResponse = typeof(UpdateResultBase<>).MakeGenericType(detail);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateSanitizePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        if (registry.CreateValidation) {
            services.Replace(ServiceDescriptor.KeyedScoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), RequestPipelineStages.Validation, typeof(ResourceCreateValidationPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        }
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(createRequest, createResponse), typeof(ResourceCreateValidateOnlyPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateSanitizePipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        if (registry.UpdateValidation) {
            services.Replace(ServiceDescriptor.KeyedScoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), RequestPipelineStages.Validation, typeof(ResourceUpdateValidationPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));
        }
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<,>).MakeGenericType(updateRequest, updateResponse), typeof(ResourceUpdateValidateOnlyPipelineAdvisor<,,>).MakeGenericType(entity, request, detail)));

        var listRequest  = typeof(ListResourceQueryRequest<,>).MakeGenericType(entity, summary);
        var listResponse = typeof(ListResultBase<,>).MakeGenericType(entity, summary);
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

    }

    private static IReadOnlyList<ResourceMethodRegistration> PrepareMethods(ResourceRegistration resource) {
        EnsureAddressablePattern(resource.Entity);
        var methods = resource.Methods.ToList();
        AddBuiltInMethods(resource, methods, resource.Entity, resource.Detail);
        foreach (var method in methods) {
            if (ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler) is null) {
                throw new InvalidOperationException(
                    $"Handler '{method.Handler.FullName}' for verb '{method.Verb}' on resource "
                    + $"'{resource.Entity.FullName}' must implement IRequestHandler<TRequest, TResponse>, "
                    + "where TRequest implements IRequest<TResponse> and IRequestPrincipal.");
            }
        }
        return methods;
    }

    private static IReadOnlyList<ServiceDescriptor> InstallMethod(IServiceCollection services, ResourceRegistration resource,
        ResourceMethodRegistration method, ResourceRegistry registry) {
        var entity = resource.Entity;
        var descriptor = ResourceMethodHandlerHelper.Describe(entity, method.Handler)!;
        var handlerInterface = ResourceMethodHandlerHelper.FindHandlerInterface(descriptor.Handler)!;
        var dependencies = new List<ServiceDescriptor>();
        Add(ServiceDescriptor.Scoped(handlerInterface, descriptor.Handler));

        var methodRequest  = descriptor.Request;
        var methodResponse = descriptor.Response;
        var envelope       = typeof(ResourceMethodRequest<,,>).MakeGenericType(entity, methodRequest, methodResponse);

        // Domain forwarders own the envelope slot when registered before the resource pipeline.
        Add(ServiceDescriptor.Scoped(typeof(IRequestHandler<,>).MakeGenericType(envelope, methodResponse),
                                    typeof(ResourceMethodDispatchHandler<,,>).MakeGenericType(entity, methodRequest, methodResponse)));
        Add(ServiceDescriptor.Scoped(
            typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, methodResponse),
            typeof(ResourceMethodResponsePipelineAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse)), true);

        if (typeof(ICanonicalName).IsAssignableFrom(methodRequest)) {
            Add(ServiceDescriptor.Scoped(
                typeof(IRequestPipelineAdvisor<,>).MakeGenericType(envelope, methodResponse),
                typeof(ResourceMethodIdempotencyPipelineAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse)), true);
            if (registry.Freshness) {
                Add(ServiceDescriptor.Scoped(typeof(IResourceMethodAdvisor<,,>).MakeGenericType(entity, methodRequest, methodResponse),
                    typeof(AdviceMethodFreshness<,,>).MakeGenericType(entity, methodRequest, methodResponse)), true);
            }
        }
        return dependencies;

        void Add(ServiceDescriptor candidate, bool enumerable = false) {
            var installed = services.FirstOrDefault(item => !item.IsKeyedService && item.ServiceType == candidate.ServiceType
                && (!enumerable || (item.ImplementationType ?? item.ImplementationInstance?.GetType()
                    ?? item.ImplementationFactory?.Method.ReturnType) == candidate.ImplementationType));
            if (installed is null) {
                if (enumerable) services.TryAddEnumerable(candidate);
                else services.TryAdd(candidate);
                installed = candidate;
            }
            dependencies.Add(installed);
        }
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
                typeof(ListResultBase<,>).MakeGenericType(entity, summary)),
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
        ResourceRegistration             resource,
        List<ResourceMethodRegistration> methods,
        Type                             entity,
        Type                             detail
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
        List<ResourceMethodRegistration> methods,
        string                           verb,
        Operations                       operation,
        Type                             handler,
        IReadOnlyList<Operations>?        allowed,
        ResourceMethodScope              scope = ResourceMethodScope.Instance
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
