using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Core;

/// <summary>
///     Reflection helpers used by the Schemata infrastructure to instantiate
///     features and invoke lifecycle methods with automatic DI resolution of
///     unmatched parameters.
/// </summary>
public static class Utilities
{
    /// <summary>
    ///     Instantiates <paramref name="type" /> via its first public constructor,
    ///     matching provided <paramref name="parameters" /> by assignable type.
    /// </summary>
    /// <typeparam name="T">Expected return type.</typeparam>
    /// <param name="type">Concrete type to instantiate.</param>
    /// <param name="parameters">Values to match against constructor parameters by type.</param>
    /// <returns>The created instance, or <see langword="default" /> if no constructor exists.</returns>
    public static T? CreateInstance<T>(Type type, params object?[] parameters) {
        return CreateInstance<T>(null, type, parameters.ToList());
    }

    /// <inheritdoc cref="CreateInstance{T}(IServiceProvider?, Type, List{object}?)" />
    public static T? CreateInstance<T>(IServiceProvider sp, Type type, params object?[] parameters) {
        return CreateInstance<T>(sp, type, parameters.ToList());
    }

    /// <summary>
    ///     Instantiates <paramref name="type" /> via constructor, resolving missing
    ///     parameters from <paramref name="sp" />. Throws when <paramref name="sp" />
    ///     is <see langword="null" /> and a parameter cannot be satisfied from
    ///     <paramref name="parameters" />.
    /// </summary>
    /// <typeparam name="T">Expected return type.</typeparam>
    /// <param name="sp">
    ///     Service provider for resolving unmatched parameters, or <see langword="null" />.
    /// </param>
    /// <param name="type">Concrete type to instantiate.</param>
    /// <param name="parameters">Values to match against constructor parameters by type.</param>
    /// <returns>The created instance, or <see langword="default" /> if no constructor exists.</returns>
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

    /// <inheritdoc cref="CallMethod(IServiceProvider?, object, string, List{object?}?)" />
    public static void CallMethod(object instance, string method, params object?[] parameters) {
        CallMethod(null, instance, method, parameters.ToList());
    }

    /// <inheritdoc cref="CallMethod(IServiceProvider?, object, string, List{object?}?)" />
    public static void CallMethod(
        IServiceProvider sp,
        object           instance,
        string           method,
        params object?[] parameters
    ) {
        CallMethod(sp, instance, method, parameters.ToList());
    }

    /// <summary>
    ///     Invokes a public instance method by name, matching provided parameters by
    ///     assignable type and resolving remaining from <paramref name="sp" />. Throws
    ///     when a parameter cannot be satisfied.
    /// </summary>
    /// <param name="sp">
    ///     Service provider for resolving unmatched parameters, or <see langword="null" />.
    /// </param>
    /// <param name="instance">The target object.</param>
    /// <param name="method">The method name.</param>
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
