// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Core.Features;

namespace Schemata.Core;

public static class SchemataOptionsExtensions
{
    public static HashSet<ISimpleFeature>? GetFeatures(this SchemataOptions options) {
        return options.Get<HashSet<ISimpleFeature>>(Constants.Options.Features);
    }

    public static void SetFeatures(this SchemataOptions options, HashSet<ISimpleFeature>? value) {
        options.Set(Constants.Options.Features, value);
    }

    public static void AddFeature<T>(this SchemataOptions options)
        where T : ISimpleFeature {
        AddFeature(options, typeof(T));
    }

    public static void AddFeature(this SchemataOptions options, Type type) {
        var value = (ISimpleFeature)Activator.CreateInstance(type)!;
        AddFeature(options, value);
    }

    public static void AddFeature(this SchemataOptions options, ISimpleFeature feature) {
        var features = GetFeatures(options) ?? [];
        features.Add(feature);
        options.SetFeatures(features);
    }
}
