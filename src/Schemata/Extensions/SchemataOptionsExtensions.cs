// ReSharper disable CheckNamespace

using System;
using System.Collections.Generic;
using Schemata.Abstractions;

namespace Schemata;

public static class SchemataOptionsExtensions
{
    public static HashSet<Type>? GetFeatures(this SchemataOptions options) {
        return options.Get<HashSet<Type>>(Constants.Options.Features);
    }

    public static void SetFeatures(this SchemataOptions options, HashSet<Type>? value) {
        options.Set(Constants.Options.Features, value);
    }

    public static void AddFeature(this SchemataOptions options, Type value) {
        var features = GetFeatures(options) ?? new HashSet<Type>();
        features.Add(value);
        options.SetFeatures(features);
    }

    public static void RemoveFeature(this SchemataOptions options, Type value) {
        var features = GetFeatures(options);
        features?.Remove(value);
        options.SetFeatures(features);
    }

    public static bool HasFeature(this SchemataOptions options, Type value) {
        var features = GetFeatures(options);
        return features?.Contains(value) ?? false;
    }
}
