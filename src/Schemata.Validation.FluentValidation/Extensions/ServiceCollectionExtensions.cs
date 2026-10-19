using System;
using System.Linq;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Validation.FluentValidation.Advisors;
using Schemata.Validation.Skeleton.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Advisors;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering FluentValidation validators and their corresponding validation advisors.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRequestValidation<TRequest, TResponse>(
        this IServiceCollection services, Operations operation) where TRequest : IRequest<TResponse> {
        services.TryAddKeyedScoped<IRequestPipelineAdvisor<TRequest, TResponse>>(
            RequestPipelineStages.Validation, (_, _) => new RequestValidationPipelineAdvisor<TRequest, TResponse>(operation));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidationErrors<>)));
        return services;
    }

    public static IServiceCollection AddStreamValidation<TRequest, TItem>(
        this IServiceCollection services, Operations operation) where TRequest : IStreamRequest<TItem> {
        services.TryAddKeyedScoped<IStreamPipelineAdvisor<TRequest, TItem>>(
            RequestPipelineStages.Validation, (_, _) => new StreamValidationPipelineAdvisor<TRequest, TItem>(operation));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidationErrors<>)));
        return services;
    }

    /// <summary>
    ///     Registers a FluentValidation validator under every closed <see cref="IValidator{T}" />
    ///     interface it implements and auto-registers <see cref="AdviceValidation{T}" /> and
    ///     <see cref="AdviceValidationErrors{T}" />. Each distinct implementation registered for the
    ///     same message type cooperates: <see cref="AdviceValidation{T}" /> runs every registered
    ///     validator once. Re-registering the same implementation for the same message type is
    ///     idempotent.
    /// </summary>
    /// <typeparam name="TValidator">The validator implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">The service lifetime for the validator registration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddValidator<TValidator>(
        this IServiceCollection services,
        ServiceLifetime         lifetime = ServiceLifetime.Scoped
    )
        where TValidator : class, IValidator {
        var implementationType = typeof(TValidator);
        var validatorTypes = implementationType.GetInterfaces()
                                               .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IValidator<>))
                                               .Distinct()
                                               .ToList();

        if (validatorTypes.Count == 0) {
            throw new AggregateException(implementationType.Name + " does not implement IValidator<>.");
        }

        foreach (var validatorType in validatorTypes) {
            AddValidator(services, validatorType, implementationType, lifetime);
        }

        return services;
    }

    /// <summary>
    ///     Registers a FluentValidation validator for a specific entity type and auto-registers the validation advisors.
    /// </summary>
    /// <typeparam name="T">The entity type being validated.</typeparam>
    /// <typeparam name="TValidator">The validator implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">The service lifetime for the validator registration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddValidator<T, TValidator>(
        this IServiceCollection services,
        ServiceLifetime         lifetime = ServiceLifetime.Scoped
    )
        where TValidator : class, IValidator<T> {
        return AddValidator(services, typeof(IValidator<T>), typeof(TValidator), lifetime);
    }

    private static IServiceCollection AddValidator(
        IServiceCollection services,
        Type               service,
        Type               implementation,
        ServiceLifetime    lifetime = ServiceLifetime.Scoped
    ) {
        services.TryAddEnumerable(new ServiceDescriptor(service, implementation, lifetime));

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidation<>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IValidationAdvisor<>), typeof(AdviceValidationErrors<>)));

        return services;
    }
}
