using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Core;

/// <summary>
///     Registry for deferred configuration actions that are accumulated during builder setup and applied later.
/// </summary>
public sealed class Configurators
{
    private readonly Dictionary<Type, object> _configurators = [];

    /// <summary>
    ///     Registers or chains a configuration action for the specified type.
    /// </summary>
    /// <typeparam name="T">The options type to configure.</typeparam>
    /// <param name="action">The configuration action.</param>
    public void Set<T>(Action<T> action) {
        var key = typeof(T);

        if (!TryGet<T>(out var configure)) {
            _configurators[key] = action;
            return;
        }

        _configurators[key] = (T options) => {
            configure!.Invoke(options);
            action(options);
        };
    }

    /// <summary>
    ///     Registers or chains a configuration action with two parameters.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <param name="action">The configuration action.</param>
    public void Set<T1, T2>(Action<T1, T2> action) {
        var key = typeof((T1, T2));

        if (!TryGet<T1, T2>(out var configure)) {
            _configurators[key] = action;
            return;
        }

        _configurators[key] = (T1 a1, T2 a2) => {
            configure!.Invoke(a1, a2);
            action(a1, a2);
        };
    }

    /// <summary>
    ///     Attempts to retrieve a configuration action for the specified type.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <param name="action">When this method returns, contains the action if found.</param>
    /// <returns><see langword="true" /> if an action was found.</returns>
    public bool TryGet<T>(out Action<T>? action) {
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

    /// <summary>
    ///     Attempts to retrieve a two-parameter configuration action.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <param name="action">When this method returns, contains the action if found.</param>
    /// <returns><see langword="true" /> if an action was found.</returns>
    public bool TryGet<T1, T2>(out Action<T1, T2>? action) {
        action = null;
        if (!_configurators.TryGetValue(typeof((T1, T2)), out var @object)) {
            return false;
        }

        if (@object is not Action<T1, T2> configure) {
            return false;
        }

        action = configure;
        return true;
    }

    /// <summary>
    ///     Retrieves a configuration action, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The configuration action.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no configurator is registered for the type.</exception>
    public Action<T> Get<T>() {
        if (TryGet<T>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }

    /// <summary>
    ///     Retrieves a two-parameter configuration action, throwing if not found.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <returns>The configuration action.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no configurator is registered for the type pair.</exception>
    public Action<T1, T2> Get<T1, T2>() {
        if (TryGet<T1, T2>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for ({typeof(T1)}, {typeof(T2)})");
    }

    /// <summary>
    ///     Retrieves and removes a configuration action, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The configuration action.</returns>
    public Action<T> Pop<T>() {
        var action = Get<T>();
        _configurators.Remove(typeof(T));
        return action;
    }

    /// <summary>
    ///     Retrieves and removes a two-parameter configuration action, throwing if not found.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <returns>The configuration action.</returns>
    public Action<T1, T2> Pop<T1, T2>() {
        var action = Get<T1, T2>();
        _configurators.Remove(typeof((T1, T2)));
        return action;
    }

    /// <summary>
    ///     Retrieves and removes a configuration action, returning a no-op if not found.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The configuration action, or a no-op action.</returns>
    public Action<T> PopOrDefault<T>() {
        if (!TryGet<T>(out var action)) {
            return _ => { };
        }

        _configurators.Remove(typeof(T));

        return action!;
    }

    /// <summary>
    ///     Retrieves and removes a two-parameter configuration action, returning a no-op if not found.
    /// </summary>
    /// <typeparam name="T1">The first parameter type.</typeparam>
    /// <typeparam name="T2">The second parameter type.</typeparam>
    /// <returns>The configuration action, or a no-op action.</returns>
    public Action<T1, T2> PopOrDefault<T1, T2>() {
        if (!TryGet<T1, T2>(out var action)) {
            return (_, _) => { };
        }

        _configurators.Remove(typeof((T1, T2)));

        return action!;
    }

    internal IServiceCollection Invoke(IServiceCollection services) {
        services.AddOptions();

        var ic = typeof(IConfigureOptions<>);
        var tc = typeof(ConfigureNamedOptions<>);
        foreach (var (type, configure) in _configurators) {
            var icg = ic.MakeGenericType(type);
            var tcg = tc.MakeGenericType(type);

            var instance = Activator.CreateInstance(tcg, Options.DefaultName, configure)!;
            services.AddSingleton(icg, _ => instance);
        }

        _configurators.Clear();

        return services;
    }
}
