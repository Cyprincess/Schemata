using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Schemata.Core;

public class Configurators
{
    private readonly Dictionary<Type, object> _configurators = new();

    public void Set<T>(Action<T> action) {
        var key = typeof(T);

        if (!TryGet<T>(out var configure)) {
            _configurators[typeof(T)] = action;
            return;
        }

        _configurators[key] = (T options) => {
            configure(options);
            action(options);
        };
    }

    public bool TryGet<T>([MaybeNullWhen(false)] out Action<T> action) {
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

    public Action<T> Get<T>() {
        if (TryGet<T>(out var action)) {
            return action;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }

    public Action<T> Pop<T>() {
        var action = Get<T>();
        _configurators.Remove(typeof(T));
        return action;
    }

    public Action<T> PopOrDefault<T>() {
        try {
            return Pop<T>();
        } catch (KeyNotFoundException) {
            return _ => { };
        }
    }

    public IServiceCollection Invoke(IServiceCollection services) {
        services.AddOptions();

        var ic = typeof(IConfigureOptions<>);
        var tc = typeof(ConfigureNamedOptions<>);
        foreach (var (type, configure) in _configurators) {
            var icg = ic.MakeGenericType(type);
            var tcg = tc.MakeGenericType(type);

            var instance = Activator.CreateInstance(tcg, Options.DefaultName, configure)!;
            services.TryAddSingleton(icg, _ => instance);
        }

        _configurators.Clear();

        return services;
    }
}
