using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Schemata.Core;

/// <summary>
///     Central options store for the Schemata framework. Provides named option
///     get/set with removal semantics, a pluggable logger factory, and is the
///     anchor for feature registration lookups.
/// </summary>
public sealed class SchemataOptions
{
    private readonly Dictionary<string, object> _options = [];
    private          ILogger<SchemataBuilder>?  _logger;

    /// <summary>
    ///     Logger factory used to create loggers for features and infrastructure
    ///     components.
    /// </summary>
    public ILoggerFactory Logging { get; private set; } = LoggerFactory.Create(_ => { });

    /// <summary>
    ///     Lazily-created default logger for the builder.
    /// </summary>
    public ILogger<SchemataBuilder> Logger => _logger ??= CreateLogger<SchemataBuilder>();

    /// <summary>
    ///     Creates a typed <see cref="ILogger{T}" /> from the current
    ///     <see cref="Logging" /> factory.
    /// </summary>
    /// <typeparam name="T">The category type for the logger.</typeparam>
    /// <returns>A <see cref="ILogger{T}" /> instance.</returns>
    public ILogger<T> CreateLogger<T>() { return Logging.CreateLogger<T>(); }

    /// <summary>
    ///     Creates a typed <c>ILogger</c> instance via <c>Logger&lt;T&gt;</c>
    ///     reflection, using the current <see cref="Logging" /> factory.
    /// </summary>
    /// <param name="type">The category type for the logger.</param>
    /// <returns>
    ///     An <see cref="ILogger" /> instance, or <see langword="null" /> if creation fails.
    /// </returns>
    public object? CreateLogger(Type type) {
        var logger  = typeof(Logger<>);
        var generic = logger.MakeGenericType(type);

        return Activator.CreateInstance(generic, Logging);
    }

    /// <summary>
    ///     Swaps the logger factory used by the builder and all features that log
    ///     through <see cref="SchemataOptions" />.
    /// </summary>
    /// <param name="factory">The replacement <see cref="ILoggerFactory" />.</param>
    public void ReplaceLoggerFactory(ILoggerFactory factory) { Logging = factory; }

    /// <summary>
    ///     Retrieves and removes a named option. Returns <see langword="null" /> when
    ///     the key is absent.
    /// </summary>
    /// <typeparam name="TOptions">Expected option type.</typeparam>
    /// <param name="name">
    ///     The key under which the option was stored via <see cref="Set{TOptions}" />.
    /// </param>
    /// <returns>The option value, or <see langword="null" />.</returns>
    public TOptions? Pop<TOptions>(string name)
        where TOptions : class {
        if (!_options.Remove(name, out var value)) {
            return null;
        }

        return value as TOptions;
    }

    /// <summary>
    ///     Retrieves a named option without removing it. Returns
    ///     <see langword="null" /> when the key is absent.
    /// </summary>
    /// <typeparam name="TOptions">Expected option type.</typeparam>
    /// <param name="name">
    ///     The key under which the option was stored via <see cref="Set{TOptions}" />.
    /// </param>
    /// <returns>The option value, or <see langword="null" />.</returns>
    public TOptions? Get<TOptions>(string name)
        where TOptions : class {
        if (!_options.TryGetValue(name, out var value)) {
            return null;
        }

        return value as TOptions;
    }

    /// <summary>
    ///     Stores a named option. Passing <see langword="null" /> removes any
    ///     existing entry for <paramref name="name" />.
    /// </summary>
    /// <typeparam name="TOptions">The option type.</typeparam>
    /// <param name="name">A stable key known to consumers.</param>
    /// <param name="options">
    ///     The value to store, or <see langword="null" /> to remove.
    /// </param>
    public void Set<TOptions>(string name, TOptions? options)
        where TOptions : class {
        if (options is null) {
            _options.Remove(name);
            return;
        }

        _options[name] = options;
    }
}
