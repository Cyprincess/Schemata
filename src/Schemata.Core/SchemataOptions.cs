using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Schemata.Core;

/// <summary>
///     Central options container for the Schemata framework, providing named option storage and logging.
/// </summary>
public sealed class SchemataOptions
{
    private readonly Dictionary<string, object> _options = [];
    private          ILogger<SchemataBuilder>?  _logger;

    /// <summary>
    ///     Gets the logger factory used to create loggers for Schemata components.
    /// </summary>
    public ILoggerFactory Logging { get; private set; } = LoggerFactory.Create(_ => { });

    /// <summary>
    ///     Gets the default logger for the Schemata builder.
    /// </summary>
    public ILogger<SchemataBuilder> Logger => _logger ??= CreateLogger<SchemataBuilder>();

    /// <summary>
    ///     Creates a typed logger using the current logging factory.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    /// <returns>A logger instance.</returns>
    public ILogger<T> CreateLogger<T>() { return Logging.CreateLogger<T>(); }

    /// <summary>
    ///     Creates a logger for the specified type using reflection.
    /// </summary>
    /// <param name="type">The type to create a logger for.</param>
    /// <returns>A logger instance, or <see langword="null" /> if creation fails.</returns>
    public object? CreateLogger(Type type) {
        var logger  = typeof(Logger<>);
        var generic = logger.MakeGenericType(type);

        return Activator.CreateInstance(generic, Logging);
    }

    /// <summary>
    ///     Replaces the current logger factory with a new one.
    /// </summary>
    /// <param name="factory">The new logger factory.</param>
    public void ReplaceLoggerFactory(ILoggerFactory factory) { Logging = factory; }

    /// <summary>
    ///     Retrieves and removes a named option from storage.
    /// </summary>
    /// <typeparam name="TOptions">The type of the option.</typeparam>
    /// <param name="name">The option key.</param>
    /// <returns>The option value, or <see langword="null" /> if not found.</returns>
    public TOptions? Pop<TOptions>(string name)
        where TOptions : class {
        if (!_options.Remove(name, out var value)) {
            return null;
        }

        return value as TOptions;
    }

    /// <summary>
    ///     Retrieves a named option from storage without removing it.
    /// </summary>
    /// <typeparam name="TOptions">The type of the option.</typeparam>
    /// <param name="name">The option key.</param>
    /// <returns>The option value, or <see langword="null" /> if not found.</returns>
    public TOptions? Get<TOptions>(string name)
        where TOptions : class {
        if (!_options.TryGetValue(name, out var value)) {
            return null;
        }

        return value as TOptions;
    }

    /// <summary>
    ///     Stores a named option, or removes it if the value is <see langword="null" />.
    /// </summary>
    /// <typeparam name="TOptions">The type of the option.</typeparam>
    /// <param name="name">The option key.</param>
    /// <param name="options">The option value to store.</param>
    public void Set<TOptions>(string name, TOptions? options)
        where TOptions : class {
        if (options is null) {
            _options.Remove(name);
            return;
        }

        _options[name] = options;
    }
}
