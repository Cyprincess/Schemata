using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Schemata.Core;

public sealed class SchemataOptions
{
    private readonly Dictionary<string, object> _options = new();
    private          ILogger<SchemataBuilder>?  _logger;

    public ILoggerFactory Logging { get; private set; } = LoggerFactory.Create(_ => { });

    public ILogger<SchemataBuilder> Logger => _logger ??= CreateLogger<SchemataBuilder>();

    public ILogger<T> CreateLogger<T>() {
        return Logging.CreateLogger<T>();
    }

    public object? CreateLogger(Type type) {
        var logger  = typeof(Logger<>);
        var generic = logger.MakeGenericType(type);

        return Activator.CreateInstance(generic, Logging);
    }

    public void ReplaceLoggerFactory(ILoggerFactory factory) {
        Logging = factory;
    }

    public TOptions? Pop<TOptions>(string name)
        where TOptions : class {
        if (!_options.Remove(name, out var value)) {
            return null;
        }

        return value as TOptions;
    }

    public TOptions? Get<TOptions>(string name)
        where TOptions : class {
        if (!_options.TryGetValue(name, out var value)) {
            return null;
        }

        return value as TOptions;
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
