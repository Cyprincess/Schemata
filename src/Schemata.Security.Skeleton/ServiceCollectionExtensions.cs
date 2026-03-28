using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAccessProvider<T, TRequest, TProvider>(this IServiceCollection services)
        where TProvider : class, IAccessProvider<T, TRequest> {
        services.TryAddScoped<IAccessProvider<T, TRequest>, TProvider>();
        return services;
    }

    public static IServiceCollection AddAccessProvider(
        this IServiceCollection services,
        Type                    entityType,
        Type                    requestType,
        Type                    providerType
    ) {
        services.TryAddScoped(typeof(IAccessProvider<,>).MakeGenericType(entityType, requestType), providerType);
        return services;
    }

    public static IServiceCollection AddEntitlementProvider(this IServiceCollection services, Type implementation) {
        services.TryAddScoped(typeof(IEntitlementProvider<,>), implementation);
        return services;
    }

    public static IServiceCollection AddPermissionResolver<TResolver>(this IServiceCollection services)
        where TResolver : class, IPermissionResolver {
        services.TryAddScoped<IPermissionResolver, TResolver>();
        return services;
    }

    public static IServiceCollection AddPermissionMatcher<TMatcher>(this IServiceCollection services)
        where TMatcher : class, IPermissionMatcher {
        services.TryAddScoped<IPermissionMatcher, TMatcher>();
        return services;
    }
}
