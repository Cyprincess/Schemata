using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Resource.Foundation;

internal static class ResourceAuthorizationRegistration
{
    private static readonly MethodInfo AddAuthenticationStandardMethod = typeof(ResourceAuthorizationRegistration)
        .GetMethod(nameof(AddAuthenticationStandard), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo AddAuthorizationStandardMethod = typeof(ResourceAuthorizationRegistration)
        .GetMethod(nameof(AddAuthorizationStandard), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo AddAuthenticationMethodMethod = typeof(ResourceAuthorizationRegistration)
        .GetMethod(nameof(AddAuthenticationMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo AddAuthorizationMethodMethod = typeof(ResourceAuthorizationRegistration)
        .GetMethod(nameof(AddAuthorizationMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static void RegisterAuthentication(
        IServiceCollection services,
        ResourceRegistration resource,
        IReadOnlyList<ResourceMethodRegistration> methods
    ) {
        AddAuthenticationStandardMethod.MakeGenericMethod(resource.Entity, resource.Request, resource.Detail, resource.Summary)
                                      .Invoke(null, [services]);
        foreach (var method in methods) {
            var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler)!;
            AddAuthenticationMethodMethod.MakeGenericMethod(resource.Entity, descriptor.Request, descriptor.Response)
                                       .Invoke(null, [services, method.Scope]);
        }
    }

    internal static void RegisterAuthorization(
        IServiceCollection services,
        ResourceRegistration resource,
        IReadOnlyList<ResourceMethodRegistration> methods
    ) {
        AddAuthorizationStandardMethod.MakeGenericMethod(resource.Entity, resource.Request, resource.Detail, resource.Summary)
                                     .Invoke(null, [services]);
        foreach (var method in methods) {
            var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler)!;
            AddAuthorizationMethodMethod.MakeGenericMethod(resource.Entity, descriptor.Request, descriptor.Response)
                                      .Invoke(null, [services, method.Scope]);
        }
    }

    private static void AddAuthenticationStandard<TEntity, TRequest, TDetail, TSummary>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        AddAuthentication<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>(services, static request => ResourceTarget.Collection(nameof(Operations.Create), typeof(TEntity)));
        AddAuthentication<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Update), typeof(TEntity), request.Name));
        AddAuthentication<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Get), typeof(TEntity), request.Request.CanonicalName ?? request.Request.Name));
        AddAuthentication<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TEntity, TSummary>>(services, static request => ResourceTarget.Collection(nameof(Operations.List), typeof(TEntity), request.Request.Parent));
        AddAuthentication<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Delete), typeof(TEntity), request.Name));
    }

    private static void AddAuthorizationStandard<TEntity, TRequest, TDetail, TSummary>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        services.TryAddKeyedScoped<ResourceAccessStage>(typeof(TEntity));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceCreateRequestAdvisor<TEntity, TRequest>, ResourceEntitlementCreateAdvisor<TEntity, TRequest>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceUpdateRequestAdvisor<TEntity, TRequest>, ResourceEntitlementUpdateAdvisor<TEntity, TRequest>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceGetRequestAdvisor<TEntity>, ResourceEntitlementGetAdvisor<TEntity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceListRequestAdvisor<TEntity>, ResourceEntitlementListAdvisor<TEntity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceDeleteRequestAdvisor<TEntity>, ResourceEntitlementDeleteAdvisor<TEntity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceUpdateAdvisor<TEntity, TRequest>, ResourceUpdateAccessAdvisor<TEntity, TRequest>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceGetAdvisor<TEntity>, ResourceGetAccessAdvisor<TEntity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceCreateAdvisor<TEntity, TRequest>, ResourceCreateAccessAdvisor<TEntity, TRequest>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceListRequestAdvisor<TEntity>, ResourceListAccessAdvisor<TEntity>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceDeleteAdvisor<TEntity>, ResourceDeleteAccessAdvisor<TEntity>>());
        AddAuthorization<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>(services, static request => ResourceTarget.Collection(nameof(Operations.Create), typeof(TEntity)));
        AddAuthorization<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Update), typeof(TEntity), request.Name));
        AddAuthorization<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Get), typeof(TEntity), request.Request.CanonicalName ?? request.Request.Name));
        AddAuthorization<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TEntity, TSummary>>(services, static request => ResourceTarget.Collection(nameof(Operations.List), typeof(TEntity), request.Request.Parent));
        AddAuthorization<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>(services, static request => ResourceTarget.Instance(nameof(Operations.Delete), typeof(TEntity), request.Name));
    }

    private static void AddAuthenticationMethod<TEntity, TRequest, TResponse>(IServiceCollection services, ResourceMethodScope scope)
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        if (scope == ResourceMethodScope.Collection) {
            AddAuthentication<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => ResourceTarget.Collection(request.Verb, typeof(TEntity)));
            return;
        }

        AddAuthentication<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TEntity), request.Name));
    }

    private static void AddAuthorizationMethod<TEntity, TRequest, TResponse>(IServiceCollection services, ResourceMethodScope scope)
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceMethodRequestAdvisor<TEntity, TRequest>, ResourceEntitlementMethodAdvisor<TEntity, TRequest>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceMethodAdvisor<TEntity, TRequest, TResponse>, ResourceMethodAccessAdvisor<TEntity, TRequest, TResponse>>());
        if (scope == ResourceMethodScope.Collection) {
            AddAuthorization<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => ResourceTarget.Collection(request.Verb, typeof(TEntity)));
            return;
        }

        AddAuthorization<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TEntity), request.Name));
    }

    private static void AddAuthentication<TRequest, TResponse>(IServiceCollection services, Func<TRequest, ResourceTarget> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, ResourceTarget>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthenticationPipelineAdvisor<TRequest, TResponse>)));
    }

    private static void AddAuthorization<TRequest, TResponse>(IServiceCollection services, Func<TRequest, ResourceTarget> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, ResourceTarget>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>)));
    }
}
