using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core;

public static class Utilities
{
    public static T? CreateInstance<T>(Type type, params object?[] parameters) {
        return CreateInstance<T>(null, type, parameters.ToList());
    }

    public static T? CreateInstance<T>(IServiceProvider sp, Type type, params object?[] parameters) {
        return CreateInstance<T>(sp, type, parameters.ToList());
    }

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
                throw new InvalidOperationException($"Cannot resolve parameter '{
                    parameter.Name
                }' of type '{
                    type.FullName
                }'.");
            }

            arguments[i] = sp.GetRequiredService(parameter.ParameterType);
        }

        return (T?)Activator.CreateInstance(type, arguments);
    }

    public static void CallMethod(object instance, string method, params object?[] parameters) {
        CallMethod(null, instance, method, parameters.ToList());
    }

    public static void CallMethod(
        IServiceProvider sp,
        object           instance,
        string           method,
        params object?[] parameters) {
        CallMethod(sp, instance, method, parameters.ToList());
    }

    public static void CallMethod(
        IServiceProvider? sp,
        object            instance,
        string            method,
        List<object?>?    parameters = null) {
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
                throw new InvalidOperationException($"Cannot resolve parameter '{
                    parameter.Name
                }' of method '{
                    method
                }' on type '{
                    instance.GetType().FullName
                }'.");
            }

            arguments[i] = sp.GetRequiredService(parameter.ParameterType);
        }

        mi.Invoke(instance, BindingFlags.DoNotWrapExceptions, null, arguments.ToArray(), null);
    }
}
