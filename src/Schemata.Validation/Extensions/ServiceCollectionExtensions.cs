using System;
using System.Linq;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Validation.Advices;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddValidator<TValidator>(
        this IServiceCollection services,
        ServiceLifetime         lifetime = ServiceLifetime.Transient)
        where TValidator : class, IValidator {
        var implementationType = typeof(TValidator);
        var validatorType = implementationType.GetInterfaces()
                                              .FirstOrDefault(t
                                                   => t.IsGenericType
                                                   && t.GetGenericTypeDefinition() == typeof(IValidator<>));

        if (validatorType == null) {
            throw new AggregateException(implementationType.Name + "is not implement with IValidator<>.");
        }

        var messageType = validatorType.GetGenericArguments().First();
        var serviceType = typeof(IValidator<>).MakeGenericType(messageType);

        return AddValidator(services, serviceType, implementationType, lifetime);
    }

    public static IServiceCollection AddValidator<T, TValidator>(
        this IServiceCollection services,
        ServiceLifetime         lifetime = ServiceLifetime.Transient)
        where TValidator : class, IValidator<T> {
        return AddValidator(services, typeof(IValidator<T>), typeof(TValidator), lifetime);
    }

    private static IServiceCollection AddValidator(
        IServiceCollection services,
        Type               service,
        Type               implementation,
        ServiceLifetime    lifetime) {
        services.TryAdd(new ServiceDescriptor(service, implementation, lifetime));

        services.TryAddTransient(typeof(IValidationAsyncAdvice<>), typeof(AdviceValidation<>));

        return services;
    }
}
