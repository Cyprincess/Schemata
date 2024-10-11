using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAccessProvider<T, TContext, TProvider>(this IServiceCollection services)
        where TProvider : class, IAccessProvider<T, TContext> {
        services.TryAddScoped<IAccessProvider<T, TContext>, TProvider>();

        return services;
    }

    public static IServiceCollection AddEntitlementProvider(this IServiceCollection services, Type implementation) {
        services.TryAddScoped(typeof(IEntitlementProvider<,>), implementation);

        return services;
    }
}
