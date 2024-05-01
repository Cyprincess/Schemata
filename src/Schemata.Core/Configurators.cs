using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Core;

public sealed class Configurators
{
    private readonly Dictionary<Type, object> _configurators = [];

    public void Set<T>(Action<T> action) {
        var key = typeof(T);

        if (!TryGet<T>(out var configure)) {
            _configurators[key] = action;
            return;
        }

        _configurators[key] = (T options) => {
            configure!.Invoke(options);
            action(options);
        };
    }

    public void Set<T1, T2>(Action<T1, T2> action) {
        var key = typeof((T1, T2));

        if (!TryGet<T1, T2>(out var configure)) {
            _configurators[key] = action;
            return;
        }

        _configurators[key] = (T1 a1, T2 a2) => {
            configure!.Invoke(a1, a2);
            action(a1, a2);
        };
    }

    public bool TryGet<T>(out Action<T>? action) {
        action = null;
        if (!_configurators.TryGetValue(typeof(T), out var @object)) {
            return false;
        }

        if (@object is not Action<T> configure) {
            return false;
        }

        action = configure;
        return true;
    }

    public bool TryGet<T1, T2>(out Action<T1, T2>? action) {
        action = null;
        if (!_configurators.TryGetValue(typeof((T1, T2)), out var @object)) {
            return false;
        }

        if (@object is not Action<T1, T2> configure) {
            return false;
        }

        action = configure;
        return true;
    }

    public Action<T> Get<T>() {
        if (TryGet<T>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }

    public Action<T1, T2> Get<T1, T2>() {
        if (TryGet<T1, T2>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for ({typeof(T1)}, {typeof(T2)})");
    }

    public Action<T> Pop<T>() {
        var action = Get<T>();
        _configurators.Remove(typeof(T));
        return action;
    }

    public Action<T1, T2> Pop<T1, T2>() {
        var action = Get<T1, T2>();
        _configurators.Remove(typeof((T1, T2)));
        return action;
    }

    public Action<T> PopOrDefault<T>() {
        if (!TryGet<T>(out var action)) {
            return _ => { };
        }

        _configurators.Remove(typeof(T));

        return action!;
    }


    public Action<T1, T2> PopOrDefault<T1, T2>() {
        if (!TryGet<T1, T2>(out var action)) {
            return (_, _) => { };
        }

        _configurators.Remove(typeof((T1, T2)));

        return action!;
    }

    public IServiceCollection Invoke(IServiceCollection services) {
        services.AddOptions();

        var ic = typeof(IConfigureOptions<>);
        var tc = typeof(ConfigureNamedOptions<>);
        foreach (var (type, configure) in _configurators) {
            var icg = ic.MakeGenericType(type);
            var tcg = tc.MakeGenericType(type);

            var instance = Activator.CreateInstance(tcg, Options.DefaultName, configure)!;
            services.AddSingleton(icg, _ => instance);
        }

        _configurators.Clear();

        return services;
    }
}
