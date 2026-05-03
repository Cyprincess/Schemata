using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Common;
using Schemata.Core.Features;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

/// <summary>
///     Feature registration and introspection extensions on
///     <see cref="SchemataOptions" />. Handles dependency resolution,
///     auto-registration of <see cref="DependsOnAttribute" /> /
///     <see cref="DependsOnAttribute{T}" /> dependencies, and
///     <see cref="InformationAttribute" /> logging.
/// </summary>
public static class SchemataOptionsExtensions
{
    /// <summary>
    ///     Retrieves the feature dictionary stored under the
    ///     <see cref="Keys.Features" /> key. Returns <see langword="null" /> when no
    ///     features have been registered.
    /// </summary>
    /// <param name="schemata">The options container.</param>
    /// <returns>The feature dictionary, or <see langword="null" />.</returns>
    public static Dictionary<RuntimeTypeHandle, ISimpleFeature>? GetFeatures(this SchemataOptions schemata) {
        return schemata.Get<Dictionary<RuntimeTypeHandle, ISimpleFeature>>(Keys.Features);
    }

    /// <summary>
    ///     Stores the given feature dictionary, replacing any previously
    ///     registered features.
    /// </summary>
    /// <param name="schemata">The options container.</param>
    /// <param name="value">
    ///     The feature dictionary to store, or <see langword="null" /> to remove.
    /// </param>
    public static void SetFeatures(
        this SchemataOptions                           schemata,
        Dictionary<RuntimeTypeHandle, ISimpleFeature>? value
    ) {
        schemata.Set(Keys.Features, value);
    }

    /// <summary>
    ///     Registers a feature by type.
    /// </summary>
    /// <typeparam name="T">The feature type to register.</typeparam>
    /// <param name="schemata">The options container.</param>
    public static void AddFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        schemata.AddFeature(typeof(T));
    }

    /// <summary>
    ///     Registers a feature by its runtime type, creating an instance with a
    ///     logger injected.
    /// </summary>
    /// <param name="schemata">The options container.</param>
    /// <param name="type">The concrete feature type.</param>
    public static void AddFeature(this SchemataOptions schemata, Type type) {
        var feature = Utilities.CreateInstance<ISimpleFeature>(type, schemata.CreateLogger(type))!;
        schemata.AddFeature(type, feature);
    }

    /// <summary>
    ///     Registers a pre-constructed feature instance. Resolves
    ///     <see cref="DependsOnAttribute" /> and
    ///     <see cref="DependsOnAttribute{T}" /> dependencies recursively, logs
    ///     <see cref="InformationAttribute" /> messages, and stores the feature
    ///     keyed by <see cref="Type.TypeHandle" />.
    /// </summary>
    /// <param name="schemata">The options container.</param>
    /// <param name="type">The feature's concrete type.</param>
    /// <param name="feature">The feature instance.</param>
    public static void AddFeature(this SchemataOptions schemata, Type type, ISimpleFeature feature) {
        var features = schemata.GetFeatures() ?? [];
        if (features.ContainsKey(type.TypeHandle)) {
            return;
        }

        var attributes = type.GetCustomAttributes(true);
        foreach (var attribute in attributes) {
            var at = attribute.GetType();

            // Only process attributes defined in Schemata.Core.Features namespace
            if (at.Namespace != "Schemata.Core.Features") {
                continue;
            }

            if (at.Name == typeof(DependsOnAttribute<>).Name) {
                var dependency = at.GenericTypeArguments[0];
                if (schemata.HasFeature(dependency)) {
                    continue;
                }

                schemata.Logger.Log(
                    LogLevel.Debug,
                    SchemataResources.GetResourceString(SchemataResources.ST0001),
                    type.Name,
                    dependency.Name
                );
                schemata.AddFeature(dependency);
                continue;
            }

            if (attribute is DependsOnAttribute depends) {
                var dependency = AppDomainTypeCache.GetType(depends.Name);
                if (dependency is not null && schemata.HasFeature(dependency)) {
                    continue;
                }

                var level    = depends.Optional ? LogLevel.Information : LogLevel.Error;
                var resource = depends.Optional ? SchemataResources.ST0003 : SchemataResources.ST0002;

                schemata.Logger.Log(level, SchemataResources.GetResourceString(resource), type.Name, depends.Name);

                continue;
            }

            if (attribute is InformationAttribute info && schemata.HasFeature<SchemataLoggingFeature>()) {
#pragma warning disable CA2254
                schemata.Logger.Log(info.Level, info.Message, info.Parameters);
#pragma warning restore CA2254
            }
        }

        features[type.TypeHandle] = feature;
        schemata.SetFeatures(features);
    }

    /// <summary>
    ///     Checks whether a feature of type <typeparamref name="T" /> is registered.
    /// </summary>
    /// <typeparam name="T">The feature type to check.</typeparam>
    /// <param name="schemata">The options container.</param>
    /// <returns><see langword="true" /> when the feature is registered.</returns>
    public static bool HasFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        return schemata.HasFeature(typeof(T));
    }

    /// <summary>
    ///     Checks whether a feature type is registered, including matching open
    ///     generic type definitions (e.g. <c>SchemataSessionFeature&lt;&gt;</c>
    ///     against a registered <c>SchemataSessionFeature&lt;T&gt;</c>).
    /// </summary>
    /// <param name="schemata">The options container.</param>
    /// <param name="type">The feature type to check.</param>
    /// <returns><see langword="true" /> when a matching feature is registered.</returns>
    public static bool HasFeature(this SchemataOptions schemata, Type type) {
        var features = schemata.GetFeatures();
        if (features is null) {
            return false;
        }

        if (features.ContainsKey(type.TypeHandle)) {
            return true;
        }

        if (!type.IsGenericTypeDefinition) {
            return false;
        }

        foreach (var handle in features.Keys) {
            var registered = Type.GetTypeFromHandle(handle);
            if (registered is not null && registered.IsGenericType && registered.GetGenericTypeDefinition() == type) {
                return true;
            }
        }

        return false;
    }
}
