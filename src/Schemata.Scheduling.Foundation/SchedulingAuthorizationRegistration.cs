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
        AddAuthentication<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(services, static request => (request.Verb, typeof(SchemataJob)));
        return services;
    }

    internal static IServiceCollection AddSchedulingAuthorization(this IServiceCollection services) {
        AddAuthorization<ResourceMethodRequest<SchemataJob, TriggerJobRequest, SchemataJobExecution>, SchemataJobExecution>(services, static request => (request.Verb, typeof(SchemataJob)));
        return services;
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
