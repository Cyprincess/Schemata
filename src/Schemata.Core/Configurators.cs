using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Core;

public class Configurators
{
    private readonly Dictionary<Type, object> _configurators = new();

    public void Set<T>(Action<T> action) {
        _configurators[typeof(T)] = action;
    }

    public Action<T> Get<T>() {
        if (_configurators.TryGetValue(typeof(T), out var action)) {
            return (Action<T>)action;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }

    public Action<T> Pop<T>() {
        var action = Get<T>();
        _configurators.Remove(typeof(T));
        return action;
    }

    public IServiceCollection Configure(IServiceCollection services) {
        services.AddOptions();

        var ic = typeof(IConfigureOptions<>);
        var tc = typeof(ConfigureNamedOptions<>);
        foreach (var (type, configure) in _configurators) {
            var icg = ic.MakeGenericType(type);
            var tcg = tc.MakeGenericType(type);

            var instance = Activator.CreateInstance(tcg, Options.DefaultName, configure)!;
            services.AddSingleton(icg, _ => instance);
        }

        return services;
    }
}
