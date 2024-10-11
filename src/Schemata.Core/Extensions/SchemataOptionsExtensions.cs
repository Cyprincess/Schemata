using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions;
using Schemata.Core.Features;

// ReSharper disable once CheckNamespace
namespace Schemata.Core;

public static class SchemataOptionsExtensions
{
    public static Dictionary<RuntimeTypeHandle, ISimpleFeature>? GetFeatures(this SchemataOptions schemata) {
        return schemata.Get<Dictionary<RuntimeTypeHandle, ISimpleFeature>>(SchemataConstants.Options.Features);
    }

    public static void SetFeatures(
        this SchemataOptions                           schemata,
        Dictionary<RuntimeTypeHandle, ISimpleFeature>? value) {
        schemata.Set(SchemataConstants.Options.Features, value);
    }

    public static void AddFeature<T>(this SchemataOptions schemata) where T : ISimpleFeature {
        AddFeature(schemata, typeof(T));
    }

    public static void AddFeature(this SchemataOptions schemata, Type type) {
        var feature = Utilities.CreateInstance<ISimpleFeature>(type, schemata.CreateLogger(type))!;
        AddFeature(schemata, type, feature);
    }

    public static void AddFeature(this SchemataOptions schemata, Type type, ISimpleFeature feature) {
        var features = GetFeatures(schemata) ?? [];
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
                if (HasFeature(schemata, dependency)) {
                    continue;
                }

                schemata.Logger.Log(LogLevel.Debug, SchemataResources.GetResourceString(SchemataResources.ST0001), type.Name, dependency.Name);
                AddFeature(schemata, dependency);
                continue;
            }

            if (attribute is DependsOnAttribute depends) {
                var dependency = AppDomainTypeCache.GetType(depends.Name);
                if (dependency is not null && HasFeature(schemata, dependency)) {
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

    public static bool HasFeature<T>(this SchemataOptions schemata) where T : ISimpleFeature {
        return HasFeature(schemata, typeof(T));
    }

    public static bool HasFeature(this SchemataOptions schemata, Type type) {
        var features = GetFeatures(schemata);
        return features?.ContainsKey(type.TypeHandle) ?? false;
    }
}
