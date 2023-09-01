// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Features;

namespace Schemata;

public static class SchemataOptionsExtensions
{
    public static HashSet<ISimpleFeature>? GetFeatures(this SchemataOptions options) {
        return options.Get<HashSet<ISimpleFeature>>(Constants.Options.Features);
    }

    public static void SetFeatures(this SchemataOptions options, HashSet<ISimpleFeature>? value) {
        options.Set(Constants.Options.Features, value);
    }

    public static void AddFeature<T>(this SchemataOptions options) {
        AddFeature(options, typeof(T));
    }

    public static void AddFeature(this SchemataOptions options, Type type) {
        var value = (ISimpleFeature)Activator.CreateInstance(type)!;
        AddFeature(options, value);
    }

    public static void AddFeature(this SchemataOptions options, ISimpleFeature value) {
        var features = GetFeatures(options) ?? new HashSet<ISimpleFeature>();
        features.Add(value);
        options.SetFeatures(features);
    }
}
