using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Scheduling.Foundation;

internal static class SchedulingAuthorizationRegistration
{
    internal static IServiceCollection AddSchedulingAuthentication(this IServiceCollection services) {
        AddAuthentication<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataJob), request.Name));
        return services;
    }

    internal static IServiceCollection AddSchedulingAuthorization(this IServiceCollection services) {
        AddAuthorization<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(services, static request => ResourceTarget.Instance(request.Verb, typeof(SchemataJob), request.Name));
        return services;
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
