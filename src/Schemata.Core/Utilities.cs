using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core;

/// <summary>
///     Reflection-based utility methods for creating instances and calling methods with automatic DI resolution.
/// </summary>
public static class Utilities
{
    /// <summary>
    ///     Creates an instance of the specified type, resolving constructor parameters from the provided values.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="type">The concrete type to instantiate.</param>
    /// <param name="parameters">Values to match against constructor parameters by type.</param>
    /// <returns>The created instance, or <see langword="default" /> if no constructor is found.</returns>
    public static T? CreateInstance<T>(Type type, params object?[] parameters) {
        return CreateInstance<T>(null, type, parameters.ToList());
    }

    /// <summary>
    ///     Creates an instance of the specified type, resolving unmatched constructor parameters from the service provider.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="sp">The service provider for resolving unmatched parameters.</param>
    /// <param name="type">The concrete type to instantiate.</param>
    /// <param name="parameters">Values to match against constructor parameters by type.</param>
    /// <returns>The created instance, or <see langword="default" /> if no constructor is found.</returns>
    public static T? CreateInstance<T>(IServiceProvider sp, Type type, params object?[] parameters) {
        return CreateInstance<T>(sp, type, parameters.ToList());
    }

    /// <summary>
    ///     Creates an instance of the specified type, resolving unmatched constructor parameters from the service provider.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="sp">The service provider for resolving unmatched parameters, or <see langword="null" />.</param>
    /// <param name="type">The concrete type to instantiate.</param>
    /// <param name="parameters">Values to match against constructor parameters by type.</param>
    /// <returns>The created instance, or <see langword="default" /> if no constructor is found.</returns>
    public static T? CreateInstance<T>(IServiceProvider? sp, Type type, List<object?>? parameters = null) {
        var ci = type.GetConstructors().FirstOrDefault();
        if (ci is null) {
            return default;
        }

        var pi        = ci.GetParameters();
        var arguments = new object?[pi.Length];
        for (var i = 0; i < pi.Length; i++) {
            var parameter = pi[i];

            var value = parameters?.FirstOrDefault(p => parameter.ParameterType.IsAssignableFrom(p?.GetType()));
            if (value is not null) {
                arguments[i] = value;
                continue;
            }

            if (sp is null) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1023), parameter.Name, type.FullName));
            }

            arguments[i] = sp.GetRequiredService(parameter.ParameterType);
        }

        return (T?)Activator.CreateInstance(type, arguments);
    }

    /// <summary>
    ///     Invokes a public instance method on the specified object, matching parameters by type.
    /// </summary>
    /// <param name="instance">The object to invoke the method on.</param>
    /// <param name="method">The name of the method to invoke.</param>
    /// <param name="parameters">Values to match against method parameters by type.</param>
    public static void CallMethod(object instance, string method, params object?[] parameters) {
        CallMethod(null, instance, method, parameters.ToList());
    }

    /// <summary>
    ///     Invokes a public instance method, resolving unmatched parameters from the service provider.
    /// </summary>
    /// <param name="sp">The service provider for resolving unmatched parameters.</param>
    /// <param name="instance">The object to invoke the method on.</param>
    /// <param name="method">The name of the method to invoke.</param>
    /// <param name="parameters">Values to match against method parameters by type.</param>
    public static void CallMethod(
        IServiceProvider sp,
        object           instance,
        string           method,
        params object?[] parameters
    ) {
        CallMethod(sp, instance, method, parameters.ToList());
    }

    /// <summary>
    ///     Invokes a public instance method, resolving unmatched parameters from the service provider.
    /// </summary>
    /// <param name="sp">The service provider for resolving unmatched parameters, or <see langword="null" />.</param>
    /// <param name="instance">The object to invoke the method on.</param>
    /// <param name="method">The name of the method to invoke.</param>
    /// <param name="parameters">Values to match against method parameters by type.</param>
    public static void CallMethod(
        IServiceProvider? sp,
        object            instance,
        string            method,
        List<object?>?    parameters = null
    ) {
        var mi = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        if (mi is null) {
            return;
        }

        var pi        = mi.GetParameters();
        var arguments = new object?[pi.Length];
        for (var i = 0; i < pi.Length; i++) {
            var parameter = pi[i];

            var value = parameters?.FirstOrDefault(p => parameter.ParameterType.IsAssignableFrom(p?.GetType()));
            if (value is not null) {
                arguments[i] = value;
                continue;
            }

            if (sp is null) {
                throw new InvalidOperationException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST1024), parameter.Name, method, instance.GetType().FullName));
            }

            arguments[i] = sp.GetRequiredService(parameter.ParameterType);
        }

        mi.Invoke(instance, BindingFlags.DoNotWrapExceptions, null, arguments.ToArray(), null);
    }
}
