using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     A typed property bag that flows through the advisor pipeline, providing shared state and access to services.
/// </summary>
public class AdviceContext
{
    private readonly Dictionary<RuntimeTypeHandle, object?> _options = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceContext" /> class.
    /// </summary>
    /// <param name="sp">The service provider used to resolve services within advisors.</param>
    public AdviceContext(IServiceProvider sp) { ServiceProvider = sp; }

    /// <summary>
    ///     Gets the service provider for resolving dependencies during pipeline execution.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    ///     Stores a value in the context, keyed by its type.
    /// </summary>
    /// <typeparam name="T">The type used as the key.</typeparam>
    /// <param name="value">The value to store.</param>
    public void Set<T>(T? value) { _options[typeof(T).TypeHandle] = value; }

    /// <summary>
    ///     Attempts to retrieve a value from the context by its type.
    /// </summary>
    /// <typeparam name="T">The type used as the key.</typeparam>
    /// <param name="value">When this method returns, contains the value if found; otherwise, the default value.</param>
    /// <returns><see langword="true" /> if a non-null value was found; otherwise, <see langword="false" />.</returns>
    public bool TryGet<T>(out T? value) {
        if (!_options.TryGetValue(typeof(T).TypeHandle, out var @object)) {
            value = default;
            return false;
        }

        value = (T?)@object;
        return value is not null;
    }

    /// <summary>
    ///     Retrieves a value from the context by its type, or throws if not found.
    /// </summary>
    /// <typeparam name="T">The type used as the key.</typeparam>
    /// <returns>The stored value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no value of the specified type exists in the context.</exception>
    public T? Get<T>() {
        if (TryGet<T>(out var value)) {
            return value;
        }

        throw new KeyNotFoundException($"No context for {typeof(T)}");
    }

    /// <summary>
    ///     Determines whether the context contains a value of the specified type.
    /// </summary>
    /// <typeparam name="T">The type to check for.</typeparam>
    /// <returns><see langword="true" /> if the context contains a value of type <typeparamref name="T" />.</returns>
    public bool Has<T>() { return _options.ContainsKey(typeof(T).TypeHandle); }
}
