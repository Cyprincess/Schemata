using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

public static class SchemataOptionsExtensions
{
    public static Dictionary<Type, ISimpleFeature>? GetFeatures(this SchemataOptions schemata) {
        return schemata.Get<Dictionary<Type, ISimpleFeature>>(Constants.Options.Features);
    }

    public static void SetFeatures(this SchemataOptions schemata, Dictionary<Type, ISimpleFeature>? value) {
        schemata.Set(Constants.Options.Features, value);
    }

    public static void AddFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        AddFeature(schemata, typeof(T));
    }

    public static void AddFeature(this SchemataOptions schemata, Type type) {
        var feature = Utilities.CreateInstance<ISimpleFeature>(type, schemata.CreateLogger(type))!;
        AddFeature(schemata, type, feature);
    }

    public static void AddFeature(this SchemataOptions schemata, Type type, ISimpleFeature feature) {
        var attributes = type.GetCustomAttributes(true);
        foreach (var attribute in attributes) {
            var at = attribute.GetType();

            if (at.Namespace != "Schemata.Core.Features") {
                continue;
            }

            if (at.Name == typeof(DependsOnAttribute<>).Name) {
                AddFeature(schemata, at.GenericTypeArguments[0]);
                continue;
            }

            if (attribute is InformationAttribute info && schemata.HasFeature<SchemataLoggingFeature>()) {
                schemata.Logger.Log(info.Level, "{Message}", info.Message);
            }
        }

        var features = GetFeatures(schemata) ?? [];
        features[type] = feature;
        schemata.SetFeatures(features);
    }

    public static bool HasFeature<T>(this SchemataOptions schemata)
        where T : ISimpleFeature {
        return HasFeature(schemata, typeof(T));
    }

    public static bool HasFeature(this SchemataOptions schemata, Type type) {
        var features = GetFeatures(schemata);
        return features?.ContainsKey(type) ?? false;
    }
}
