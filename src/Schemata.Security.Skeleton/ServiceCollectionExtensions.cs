using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers security providers in an <see cref="IServiceCollection"/>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers a typed access provider.</summary>
    /// <typeparam name="T">Entity type handled by the provider.</typeparam>
    /// <typeparam name="TRequest">Request payload type handled by the provider.</typeparam>
    /// <typeparam name="TProvider">Access provider implementation type.</typeparam>
    /// <param name="services">Service collection receiving the registration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddAccessProvider<T, TRequest, TProvider>(this IServiceCollection services)
        where TProvider : class, IAccessProvider<T, TRequest> {
        services.TryAddScoped<IAccessProvider<T, TRequest>, TProvider>();
        return services;
    }

    /// <summary>Registers an access provider for runtime entity and request types.</summary>
    /// <param name="services">Service collection receiving the registration.</param>
    /// <param name="entityType">Entity type handled by the provider.</param>
    /// <param name="requestType">Request payload type handled by the provider.</param>
    /// <param name="providerType">Access provider implementation type.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddAccessProvider(
        this IServiceCollection services,
        Type                    entityType,
        Type                    requestType,
        Type                    providerType
    ) {
        services.TryAddScoped(typeof(IAccessProvider<,>).MakeGenericType(entityType, requestType), providerType);
        return services;
    }

    /// <summary>Registers an open generic entitlement provider implementation.</summary>
    /// <param name="services">Service collection receiving the registration.</param>
    /// <param name="implementation">Open generic entitlement provider implementation type.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddEntitlementProvider(this IServiceCollection services, Type implementation) {
        services.TryAddScoped(typeof(IEntitlementProvider<,>), implementation);
        return services;
    }

    /// <summary>Registers a permission resolver.</summary>
    /// <typeparam name="TResolver">Permission resolver implementation type.</typeparam>
    /// <param name="services">Service collection receiving the registration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPermissionResolver<TResolver>(this IServiceCollection services)
        where TResolver : class, IPermissionResolver {
        services.TryAddScoped<IPermissionResolver, TResolver>();
        return services;
    }

    /// <summary>Registers a permission matcher.</summary>
    /// <typeparam name="TMatcher">Permission matcher implementation type.</typeparam>
    /// <param name="services">Service collection receiving the registration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPermissionMatcher<TMatcher>(this IServiceCollection services)
        where TMatcher : class, IPermissionMatcher {
        services.TryAddScoped<IPermissionMatcher, TMatcher>();
        return services;
    }
}
