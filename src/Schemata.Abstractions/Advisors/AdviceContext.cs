using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Advisors;

/// <summary>
///     Typed property bag flowing through the advisor pipeline.
///     Advisors share state via <see cref="Set{T}" />, <see cref="TryGet{T}" />,
///     and <see cref="Has{T}" />.
/// </summary>
public class AdviceContext
{
    private readonly Dictionary<RuntimeTypeHandle, object?> _options = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceContext" /> class.
    /// </summary>
    /// <param name="sp">The <see cref="IServiceProvider" /> available to advisors during pipeline execution.</param>
    public AdviceContext(IServiceProvider sp) { ServiceProvider = sp; }

    /// <summary>
    ///     The <see cref="IServiceProvider" /> for resolving services within advisors.
    /// </summary>
    public IServiceProvider ServiceProvider { get; }

    /// <summary>
    ///     Stores a value keyed by its runtime type.
    /// </summary>
    /// <typeparam name="T">The type used as the key.</typeparam>
    /// <param name="value">The value to store; may be <see langword="null" />.</param>
    public void Set<T>(T? value) { _options[typeof(T).TypeHandle] = value; }

    /// <summary>
    ///     Attempts to retrieve a value keyed by type.
    /// </summary>
    /// <typeparam name="T">The key type.</typeparam>
    /// <param name="value">
    ///     When this method returns <see langword="true" />, contains the stored value;
    ///     otherwise <see langword="null" />.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if a non-null value was found for type
    ///     <typeparamref name="T" />; otherwise <see langword="false" />.
    /// </returns>
    public bool TryGet<T>(out T? value) {
        if (!_options.TryGetValue(typeof(T).TypeHandle, out var @object)) {
            value = default;
            return false;
        }

        value = (T?)@object;
        return value is not null;
    }

    /// <summary>
    ///     Retrieves a value keyed by type, throwing if none exists.
    /// </summary>
    /// <typeparam name="T">The key type.</typeparam>
    /// <returns>The stored value.</returns>
    /// <exception cref="KeyNotFoundException">
    ///     No value of type <typeparamref name="T" /> has been set in this context.
    /// </exception>
    public T? Get<T>() {
        if (TryGet<T>(out var value)) {
            return value;
        }

        throw new KeyNotFoundException($"No context for {typeof(T)}");
    }

    /// <summary>
    ///     Determines whether a value of type <typeparamref name="T" /> exists in the
    ///     context, regardless of whether the stored value is <see langword="null" />.
    /// </summary>
    /// <typeparam name="T">The key type.</typeparam>
    /// <returns>
    ///     <see langword="true" /> if a key for type <typeparamref name="T" /> exists.
    /// </returns>
    public bool Has<T>() { return _options.ContainsKey(typeof(T).TypeHandle); }
}
