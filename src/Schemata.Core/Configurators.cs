using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Schemata.Core;

/// <summary>
///     Deferred configurator registry. Configuration actions registered during
///     builder setup are accumulated here and applied later via <see cref="Invoke" />,
///     which wraps each action as a <see cref="ConfigureNamedOptions{TOptions}" />
///     singleton.
/// </summary>
public sealed class Configurators
{
    private readonly Dictionary<Type, object> _configurators = [];

    /// <summary>
    ///     Records an options-configuration delegate. If a delegate of the same
    ///     type already exists, the new delegate is chained after the existing one.
    /// </summary>
    /// <typeparam name="T">The options type keyed by <c>typeof(T)</c>.</typeparam>
    /// <param name="action">The configuration delegate to record.</param>
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
    ///     Records a two-parameter configuration delegate (e.g. an
    ///     <see cref="AuthenticationBuilder" /> callback), keyed by the tuple
    ///     <c>(T1, T2)</c>.
    /// </summary>
    /// <param name="action">The configuration delegate to record.</param>
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
    ///     Attempts to retrieve a registered configuration action without throwing.
    /// </summary>
    /// <typeparam name="T">The options type keyed by <c>typeof(T)</c>.</typeparam>
    /// <param name="action">The registered action, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when an action was found for <typeparamref name="T" />.</returns>
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
    ///     Two-parameter variant of <see cref="TryGet{T}(out Action{T})" />, keyed
    ///     by the tuple <c>(T1, T2)</c>.
    /// </summary>
    /// <param name="action">The registered action, or <see langword="null" /> when absent.</param>
    /// <returns><see langword="true" /> when an action was found for <c>(T1, T2)</c>.</returns>
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
    ///     Retrieves a registered configuration action, throwing
    ///     <see cref="KeyNotFoundException" /> when absent.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The registered action.</returns>
    public Action<T> Get<T>() {
        if (TryGet<T>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for {typeof(T)}");
    }

    /// <summary>
    ///     Two-parameter variant of <see cref="Get{T}" />, keyed by the tuple
    ///     <c>(T1, T2)</c>.
    /// </summary>
    /// <returns>The registered action.</returns>
    public Action<T1, T2> Get<T1, T2>() {
        if (TryGet<T1, T2>(out var action)) {
            return action!;
        }

        throw new KeyNotFoundException($"No configurator for ({typeof(T1)}, {typeof(T2)})");
    }

    /// <summary>
    ///     Retrieves and removes a configuration action, throwing when absent.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The registered action.</returns>
    public Action<T> Pop<T>() {
        var action = Get<T>();
        _configurators.Remove(typeof(T));
        return action;
    }

    /// <summary>
    ///     Two-parameter variant of <see cref="Pop{T}" />. Retrieves and removes,
    ///     throwing when absent.
    /// </summary>
    /// <returns>The registered action.</returns>
    public Action<T1, T2> Pop<T1, T2>() {
        var action = Get<T1, T2>();
        _configurators.Remove(typeof((T1, T2)));
        return action;
    }

    /// <summary>
    ///     Retrieves and removes a configuration action, returning a no-op when
    ///     absent.
    /// </summary>
    /// <typeparam name="T">The options type.</typeparam>
    /// <returns>The registered action, or a no-op.</returns>
    public Action<T> PopOrDefault<T>() {
        if (!TryGet<T>(out var action)) {
            return _ => { };
        }

        _configurators.Remove(typeof(T));

        return action!;
    }

    /// <summary>
    ///     Two-parameter variant of <see cref="PopOrDefault{T}" />. Retrieves and
    ///     removes, returning a no-op when absent.
    /// </summary>
    /// <returns>The registered action, or a no-op.</returns>
    public Action<T1, T2> PopOrDefault<T1, T2>() {
        if (!TryGet<T1, T2>(out var action)) {
            return (_, _) => { };
        }

        _configurators.Remove(typeof((T1, T2)));

        return action!;
    }

    /// <summary>
    ///     Wraps every outstanding configurator as a
    ///     <see cref="ConfigureNamedOptions{TOptions}" /> singleton registered in the
    ///     service collection, then clears the registry.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The given service collection for chaining.</returns>
    public IServiceCollection Invoke(IServiceCollection services) {
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
