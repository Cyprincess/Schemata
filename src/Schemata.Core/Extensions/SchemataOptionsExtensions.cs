using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Common;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

/// <summary>
///     Extension methods for managing features within <see cref="SchemataOptions" />.
/// </summary>
public static class SchemataOptionsExtensions
{
    /// <summary>
    ///     Gets the registered features dictionary from the options.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <returns>The features dictionary, or <see langword="null" /> if none are registered.</returns>
    public static Dictionary<RuntimeTypeHandle, ISimpleFeature>? GetFeatures(this SchemataOptions schemata) {
        return schemata.Get<Dictionary<RuntimeTypeHandle, ISimpleFeature>>(SchemataConstants.Options.Features);
    }

    /// <summary>
    ///     Stores the features dictionary in the options.
    /// </summary>
    public static void SetFeatures(
        this SchemataOptions                           schemata,
        Dictionary<RuntimeTypeHandle, ISimpleFeature>? value
    ) {
        schemata.Set(SchemataConstants.Options.Features, value);
    }

    /// <summary>
    ///     Registers a feature by type.
    /// </summary>
    /// <typeparam name="T">The feature type to register.</typeparam>
    /// <param name="schemata">The Schemata options.</param>
    public static void AddFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        schemata.AddFeature(typeof(T));
    }

    /// <summary>
    ///     Registers a feature by runtime type, creating an instance via reflection.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="type">The feature type to register.</param>
    public static void AddFeature(this SchemataOptions schemata, Type type) {
        var feature = Utilities.CreateInstance<ISimpleFeature>(type, schemata.CreateLogger(type))!;
        schemata.AddFeature(type, feature);
    }

    /// <summary>
    ///     Registers a feature instance, resolving dependencies and logging information attributes.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="type">The feature type.</param>
    /// <param name="feature">The feature instance to register.</param>
    public static void AddFeature(this SchemataOptions schemata, Type type, ISimpleFeature feature) {
        var features = schemata.GetFeatures() ?? [];
        if (features.ContainsKey(type.TypeHandle)) {
            return;
        }

        var attributes = type.GetCustomAttributes(true);
        foreach (var attribute in attributes) {
            var at = attribute.GetType();

            if (at.Namespace != "Schemata.Core.Features") {
                continue;
            }

            if (at.Name == typeof(DependsOnAttribute<>).Name) {
                var dependency = at.GenericTypeArguments[0];
                if (schemata.HasFeature(dependency)) {
                    continue;
                }

                schemata.Logger.Log(LogLevel.Debug, SchemataResources.GetResourceString(SchemataResources.ST0001),
                                    type.Name, dependency.Name);
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
    ///     Checks whether a feature is registered by type.
    /// </summary>
    /// <typeparam name="T">The feature type to check for.</typeparam>
    /// <param name="schemata">The Schemata options.</param>
    /// <returns><see langword="true" /> if the feature is registered.</returns>
    public static bool HasFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        return schemata.HasFeature(typeof(T));
    }

    /// <summary>
    ///     Checks whether a feature is registered by runtime type, including open generic definitions.
    /// </summary>
    /// <param name="schemata">The Schemata options.</param>
    /// <param name="type">The feature type or open generic type definition to check for.</param>
    /// <returns><see langword="true" /> if the feature is registered.</returns>
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
