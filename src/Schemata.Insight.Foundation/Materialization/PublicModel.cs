using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Foundation.Planning;

namespace Schemata.Insight.Foundation.Materialization;

internal sealed class PublicModel
{
    private static readonly ConcurrentDictionary<Type, PublicModel> Models = new();
    private readonly ConditionalWeakTable<FieldDescriptor[], PropertyInfo[]> _bindings = new();
    private readonly ConditionalWeakTable<FieldDescriptor[], PropertyInfo[]>.CreateValueCallback _bind;
    private readonly Dictionary<string, PropertyInfo> _properties;
    private readonly HashSet<string> _declared;

    private PublicModel(Type type) {
        Type = type;
        _bind = BindProperties;
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        _declared = properties.Select(p => p.Name).Concat(type.GetFields(BindingFlags.Instance | BindingFlags.Public).Select(f => f.Name)).ToHashSet(StringComparer.Ordinal);
        _properties = properties.Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .ToDictionary(p => p.Name.Underscore(), StringComparer.Ordinal);
    }

    internal Type Type { get; }
    internal IEnumerable<KeyValuePair<string, PropertyInfo>> Properties => _properties;
    internal static PublicModel For(Type type) => Models.GetOrAdd(type, static t => new(t));
    internal bool Declares(string name) => _declared.Contains(name.Pascalize());

    internal PropertyInfo Property(string name) {
        if (_properties.TryGetValue(name.Underscore(), out var property)) return property;
        throw Invalid(name);
    }

    internal PropertyInfo[] Bind(ImmutableArray<FieldDescriptor> fields) => _bindings.GetValue(
        ImmutableCollectionsMarshal.AsArray(fields)!, _bind);

    private PropertyInfo[] BindProperties(FieldDescriptor[] fields) {
        var properties = new PropertyInfo[fields.Length];
        for (var i = 0; i < fields.Length; i++) properties[i] = Property(fields[i].Name);
        return properties;
    }

    internal Type Resolve(string path) {
        var type = Type;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            if (MapValue(type) is { } map) type = map;
            else if (Element(type) is { } element && int.TryParse(segment, out _)) type = element;
            else type = For(type).Property(segment).PropertyType;
        }
        return type;
    }

    internal static Type? MapValue(Type type) => type.GetInterfaces().Append(type).FirstOrDefault(i => i.IsGenericType
        && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))?.GetGenericArguments()[1];

    internal static IEnumerable<KeyValuePair<string, object?>> MapEntries(object value) {
        if (value is IDictionary dictionary) {
            foreach (DictionaryEntry entry in dictionary) {
                if (entry.Key is not string key) throw Invalid("non-string map key");
                yield return new(key, entry.Value);
            }
            yield break;
        }
        PropertyInfo? keyProperty = null;
        PropertyInfo? valueProperty = null;
        foreach (var pair in (IEnumerable)value) {
            if (keyProperty is null) {
                var model = For(pair!.GetType());
                keyProperty = model.Property("key");
                valueProperty = model.Property("value");
            }
            if (keyProperty.GetValue(pair) is not string key) throw Invalid("non-string map key");
            yield return new(key, valueProperty!.GetValue(pair));
        }
    }

    internal static Type? Element(Type type) {
        if (type == typeof(string) || type == typeof(byte[]) || MapValue(type) is not null) return null;
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return type.GetGenericArguments()[0];
        return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    internal IReadOnlyDictionary<string, object?> Materialize(object value) => Materialize(value, new(ReferenceEqualityComparer.Instance));

    private IReadOnlyDictionary<string, object?> Materialize(object value, HashSet<object> active) {
        if (!active.Add(value)) throw Invalid("cyclic object graph");
        try {
            var row = new Dictionary<string, object?>(_properties.Count, StringComparer.Ordinal);
            foreach (var (name, property) in _properties) row.Add(name, Normalize(property.GetValue(value), property.PropertyType, active));
            return row;
        } finally { active.Remove(value); }
    }

    private static object? Normalize(object? value, Type declared, HashSet<object> active) {
        if (value is null) return null;
        declared = Nullable.GetUnderlyingType(declared) ?? declared;
        if (InsightValueModel.ScalarType(declared) != FieldType.Object) {
            InsightValueModel.NormalizeScalar(value);
            return value;
        }
        if (declared.IsPrimitive) throw InsightValueModel.Unsupported(declared);
        if (declared == typeof(object)) throw Invalid("untyped object");
        if (MapValue(declared) is { } mapValue) {
            if (!active.Add(value)) throw Invalid("cyclic map");
            try {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var (key, item) in MapEntries(value)) result.Add(key, Normalize(item, mapValue, active));
                return result;
            } finally { active.Remove(value); }
        }
        if (Element(declared) is { } element) {
            if (!active.Add(value)) throw Invalid("cyclic collection");
            try {
                var values = new List<object?>();
                foreach (var item in (IEnumerable)value) values.Add(Normalize(item, element, active));
                return values;
            } finally { active.Remove(value); }
        }
        return For(declared).Materialize(value, active);
    }

    private static InsightValidationException Invalid(string field) => new(InsightReasons.InvalidArgument,
        SchemataResources.INSIGHT_PUBLIC_FIELD_INVALID, new Dictionary<string, string?> { ["field"] = field });
}
