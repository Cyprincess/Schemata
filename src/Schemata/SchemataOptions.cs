using System.Collections.Generic;

namespace Schemata;

public class SchemataOptions
{
    private readonly Dictionary<string, object> _options = new();

    public TOptions? Get<TOptions>(string name)
        where TOptions : class {
        return _options.TryGetValue(name, out var value) ? (TOptions)value : null;
    }

    public void Set<TOptions>(string name, TOptions? options)
        where TOptions : class {
        if (options is null) {
            _options.Remove(name);
            return;
        }

        _options[name] = options;
    }
}
