using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
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

    internal static void AddResourceAuthorizationAdvisors(IServiceCollection services) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceCreateRequestAdvisor<,>), typeof(ResourceEntitlementCreateAdvisor<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateRequestAdvisor<,>), typeof(ResourceEntitlementUpdateAdvisor<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetRequestAdvisor<>), typeof(ResourceEntitlementGetAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(ResourceEntitlementListAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteRequestAdvisor<>), typeof(ResourceEntitlementDeleteAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodRequestAdvisor<,>), typeof(ResourceEntitlementMethodAdvisor<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceUpdateAdvisor<,>), typeof(ResourceUpdateAccessAdvisor<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceGetAdvisor<>), typeof(ResourceGetAccessAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceListRequestAdvisor<>), typeof(ResourceListAccessAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceDeleteAdvisor<>), typeof(ResourceDeleteAccessAdvisor<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceMethodAdvisor<,,>), typeof(ResourceMethodAccessAdvisor<,,>)));
        
    }

    internal static void RegisterAuthentication(
        IServiceCollection services,
        ResourceAttribute resource,
        IReadOnlyList<ResourceMethodAttribute> methods
    ) {
        AddAuthenticationStandardMethod.MakeGenericMethod(resource.Entity, resource.Request, resource.Detail, resource.Summary)
                                      .Invoke(null, [services]);
        foreach (var method in methods) {
            var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler)!;
            AddAuthenticationMethodMethod.MakeGenericMethod(resource.Entity, descriptor.Request, descriptor.Response)
                                       .Invoke(null, [services]);
        }
    }

    internal static void RegisterAuthorization(
        IServiceCollection services,
        ResourceAttribute resource,
        IReadOnlyList<ResourceMethodAttribute> methods
    ) {
        AddAuthorizationStandardMethod.MakeGenericMethod(resource.Entity, resource.Request, resource.Detail, resource.Summary)
                                     .Invoke(null, [services]);
        foreach (var method in methods) {
            var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler)!;
            AddAuthorizationMethodMethod.MakeGenericMethod(resource.Entity, descriptor.Request, descriptor.Response)
                                      .Invoke(null, [services]);
        }
    }

    private static void AddAuthenticationStandard<TEntity, TRequest, TDetail, TSummary>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        AddAuthentication<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>(services, static _ => (nameof(Operations.Create), typeof(TEntity)));
        AddAuthentication<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>(services, static _ => (nameof(Operations.Update), typeof(TEntity)));
        AddAuthentication<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>(services, static _ => (nameof(Operations.Get), typeof(TEntity)));
        AddAuthentication<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TSummary>>(services, static _ => (nameof(Operations.List), typeof(TEntity)));
        AddAuthentication<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>(services, static _ => (nameof(Operations.Delete), typeof(TEntity)));
    }

    private static void AddAuthorizationStandard<TEntity, TRequest, TDetail, TSummary>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        AddAuthorization<CreateResourceRequest<TEntity, TRequest, TDetail>, CreateResultBase<TDetail>>(services, static _ => (nameof(Operations.Create), typeof(TEntity)));
        AddAuthorization<UpdateResourceRequest<TEntity, TRequest, TDetail>, UpdateResultBase<TDetail>>(services, static _ => (nameof(Operations.Update), typeof(TEntity)));
        AddAuthorization<GetResourceQueryRequest<TEntity, TDetail>, GetResultBase<TDetail>>(services, static _ => (nameof(Operations.Get), typeof(TEntity)));
        AddAuthorization<ListResourceQueryRequest<TEntity, TSummary>, ListResultBase<TSummary>>(services, static _ => (nameof(Operations.List), typeof(TEntity)));
        AddAuthorization<DeleteResourceRequest<TEntity, TDetail>, DeleteResultBase<TDetail>>(services, static _ => (nameof(Operations.Delete), typeof(TEntity)));
    }

    private static void AddAuthenticationMethod<TEntity, TRequest, TResponse>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        AddAuthentication<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => (request.Verb, typeof(TEntity)));
    }

    private static void AddAuthorizationMethod<TEntity, TRequest, TResponse>(IServiceCollection services)
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        AddAuthorization<ResourceMethodRequest<TEntity, TRequest, TResponse>, TResponse>(services, static request => (request.Verb, typeof(TEntity)));
    }

    private static void AddAuthentication<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthenticationPipelineAdvisor<TRequest, TResponse>)));
    }

    private static void AddAuthorization<TRequest, TResponse>(IServiceCollection services, Func<TRequest, (string Operation, Type? Entity)> resolve)
        where TRequest : IRequest<TResponse>, IRequestPrincipal {
        services.TryAddScoped<Func<TRequest, (string Operation, Type? Entity)>>(_ => resolve);
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IRequestPipelineAdvisor<TRequest, TResponse>), typeof(AuthorizationPipelineAdvisor<TRequest, TResponse>)));
    }
}
