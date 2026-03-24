using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Security.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering security providers with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a custom <see cref="IAccessProvider{T, TContext}"/> implementation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <typeparam name="TProvider">The access provider implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAccessProvider<T, TContext, TProvider>(this IServiceCollection services)
        where TProvider : class, IAccessProvider<T, TContext> {
        services.TryAddScoped<IAccessProvider<T, TContext>, TProvider>();

        return services;
    }

    /// <summary>
    ///     Registers a custom <see cref="IAccessProvider{T, TContext}"/> using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="entityType">The entity type.</param>
    /// <param name="contextType">The context type.</param>
    /// <param name="providerType">The access provider implementation type.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAccessProvider(
        this IServiceCollection services,
        Type                    entityType,
        Type                    contextType,
        Type                    providerType
    ) {
        services.TryAddScoped(typeof(IAccessProvider<,>).MakeGenericType(entityType, contextType), providerType);

        return services;
    }

    /// <summary>
    ///     Registers a custom <see cref="IEntitlementProvider{T, TContext}"/> using its runtime type.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="implementation">The entitlement provider implementation type.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEntitlementProvider(this IServiceCollection services, Type implementation) {
        services.TryAddScoped(typeof(IEntitlementProvider<,>), implementation);

        return services;
    }
}
