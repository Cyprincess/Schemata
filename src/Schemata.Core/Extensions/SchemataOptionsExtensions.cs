// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
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
        var feature = (ISimpleFeature)Utilities.CreateInstance(type, Utilities.CreateLogger(options.Logger, type))!;
        AddFeature(options, type, feature);
    }

    public static void AddFeature(this SchemataOptions options, Type type, ISimpleFeature feature) {
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
