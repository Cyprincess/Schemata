using System;
using System.Collections.Generic;

namespace Schemata;

public class Configurators
{
    private readonly Dictionary<Type, object> _configurators = new();

    public void TryAdd<T>(Action<T> action) {
        if (!_configurators.ContainsKey(typeof(T))) {
            return;
        }

        _configurators.Add(typeof(T), action);
    }

    public Action<T> Get<T>() {
        if (_configurators.TryGetValue(typeof(T), out var action)) {
            return (Action<T>)action;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }
}
