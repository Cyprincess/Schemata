// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Core.Features;

namespace Schemata.Core;

public static class SchemataOptionsExtensions
{
    public static Dictionary<Type, ISimpleFeature>? GetFeatures(this SchemataOptions options) {
        return options.Get<Dictionary<Type, ISimpleFeature>>(Constants.Options.Features);
    }

    public static void SetFeatures(this SchemataOptions options, Dictionary<Type, ISimpleFeature>? value) {
        options.Set(Constants.Options.Features, value);
    }

    public static void AddFeature<T>(this SchemataOptions options)
        where T : ISimpleFeature {
        AddFeature(options, typeof(T));
    }

    public static void AddFeature(this SchemataOptions options, Type type) {
        var feature = Utilities.CreateInstance<ISimpleFeature>(type, options.CreateLogger(type))!;
        AddFeature(options, type, feature);
    }

    public static void AddFeature(this SchemataOptions options, Type type, ISimpleFeature feature) {
        var attributes = type.GetCustomAttributes();
        foreach (var attribute in attributes) {
            var at = attribute.GetType();

            if (at.Namespace != "Schemata.Core.Features") {
                continue;
            }

            if (at.IsGenericType && at.GetGenericTypeDefinition() == typeof(DependsOnAttribute<>)) {
                AddFeature(options, at.GenericTypeArguments[0]);
                continue;
            }

            if (attribute is InformationAttribute info && options.HasFeature<SchemataLoggingFeature>()) {
                options.Logger.Log(info.Level, "{Message}", info.Message);
            }
        }

        var features = GetFeatures(options) ?? [];
        features[type] = feature;
        options.SetFeatures(features);
    }

    public static bool HasFeature<T>(this SchemataOptions options)
        where T : ISimpleFeature {
        return HasFeature(options, typeof(T));
    }

    public static bool HasFeature(this SchemataOptions options, Type type) {
        var features = GetFeatures(options);
        return features?.ContainsKey(type) ?? false;
    }
}
