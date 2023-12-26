using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Schemata;

public static class InvokerUtilities
{
    public static void CallMethod(object instance, string method, params object?[] parameters) {
        CallMethod(null, instance, method, parameters.ToList());
    }

    public static void CallMethod(
        IServiceProvider provider,
        object           instance,
        string           method,
        params object?[] parameters) {
        CallMethod(provider, instance, method, parameters.ToList());
    }

    public static void CallMethod(
        IServiceProvider? provider,
        object            instance,
        string            method,
        List<object?>?    parameters = null) {
        var mi = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
        if (mi is null) return;
        var pi        = mi.GetParameters();
        var arguments = new object?[pi.Length];
        for (var i = 0; i < pi.Length; i++) {
            var parameter = pi[i];

            if (parameters?.FirstOrDefault(p => parameter.ParameterType.IsAssignableFrom(p?.GetType())) is not null) {
                arguments[i] = parameters[i];
                continue;
            }

            if (provider is null) {
                throw new InvalidOperationException($"Cannot resolve parameter '{
                    parameter.Name
                }' of method '{
                    method
                }' on type '{
                    instance.GetType().FullName
                }'.");
            }

            arguments[i] = provider.GetRequiredService(parameter.ParameterType);
        }

        mi.Invoke(instance, BindingFlags.DoNotWrapExceptions, null, arguments?.ToArray(), null);
    }
}
