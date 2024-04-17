using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Advices;

public class AdviceContext
{
    private readonly Dictionary<RuntimeTypeHandle, object?> _options = [];

    public void Set<T>(T? value) {
        _options[typeof(T).TypeHandle] = value;
    }

    public bool TryGet<T>(out T? value) {
        if (!_options.TryGetValue(typeof(T).TypeHandle, out var @object)) {
            value = default;
            return false;
        }

        value = (T?)@object;
        return value is not null;
    }

    public T? Get<T>() {
        if (TryGet<T>(out var value)) {
            return value;
        }

        throw new KeyNotFoundException($"No context for {typeof(T)}");
    }

    public bool Has<T>() {
        return _options.ContainsKey(typeof(T).TypeHandle);
    }
}
