using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Resource;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;
using Schemata.Messaging.Skeleton.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Advisors;

namespace Schemata.Scheduling.Foundation;

internal static class SchedulingAuthorizationRegistration<TJob, TExecution>
    where TJob : SchemataJob, new()
    where TExecution : SchemataJobExecution, new()
{
    internal static IServiceCollection AddSchedulingAuthentication(IServiceCollection services) {
        AddAuthentication<ResourceMethodRequest<TJob, RunJobRequest, Operation>, Operation>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TJob), request.Name));
        return services;
    }

    internal static IServiceCollection AddSchedulingAuthorization(IServiceCollection services) {
        services.TryAddScoped<SchedulingAccessPolicy>();
        services.TryAddKeyedScoped<IResourceTargetPolicy>(typeof(TJob), (provider, _) => provider.GetRequiredService<SchedulingAccessPolicy>());
        AddAuthorization<ResourceMethodRequest<TJob, RunJobRequest, Operation>, Operation>(services, static request => ResourceTarget.Instance(request.Verb, typeof(TJob), request.Name));
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
