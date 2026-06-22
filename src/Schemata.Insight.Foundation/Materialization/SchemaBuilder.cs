using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Humanizer;
using Schemata.Common;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Foundation;

public static class SchemaBuilder
{
    public static IReadOnlyList<FieldDescriptor> For(
        Type                          entityType,
        ImmutableArray<SelectionItem> items,
        string                        alias
    ) {
        if (items.IsDefaultOrEmpty) {
            var fields = new List<FieldDescriptor>();
            foreach (var property in AppDomainTypeCache.GetProperties(entityType).Values) {
                fields.Add(new(property.Name.Underscore(), MapType(property.PropertyType), alias, false, []));
            }

            return fields;
        }

        var selected = new List<FieldDescriptor>(items.Length);
        foreach (var item in items) {
            switch (item.Kind) {
                case SelectionKind.Field when !string.IsNullOrWhiteSpace(item.FieldPath):
                    selected.Add(new(item.Alias, MapType(ResolveType(entityType, StripAlias(item.FieldPath, alias))), alias, false, []));
                    break;
                case SelectionKind.Expression:
                    selected.Add(new(item.Alias, FieldType.Object, null, false, []));
                    break;
                case SelectionKind.Nested when !string.IsNullOrWhiteSpace(item.FieldPath):
                    selected.Add(NestedDescriptor(entityType, item, alias));
                    break;
            }
        }

        return selected;
    }

    private static FieldDescriptor NestedDescriptor(Type parentType, SelectionItem item, string alias) {
        var childType = ElementType(ResolveType(parentType, StripAlias(item.FieldPath!, alias)));
        var children  = childType is null ? [] : For(childType, item.Children, item.Alias);
        return new(item.Alias, FieldType.Object, null, true, [..children]);
    }

    private static Type? ElementType(Type type) {
        if (type.IsArray) {
            return type.GetElementType();
        }

        foreach (var contract in type.GetInterfaces()) {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Type ResolveType(Type type, string path) {
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
            var property = AppDomainTypeCache.GetProperty(type, segment.Pascalize());
            if (property is null) {
                return typeof(object);
            }

            type = property.PropertyType;
        }

        return type;
    }

    private static FieldType MapType(Type type) {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(Guid)) return FieldType.String;
        if (type == typeof(bool)) return FieldType.Bool;
        if (type == typeof(byte)
         || type == typeof(sbyte)
         || type == typeof(short)
         || type == typeof(ushort)
         || type == typeof(int)
         || type == typeof(uint)
         || type == typeof(long)
         || type == typeof(ulong)) return FieldType.Int64;
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return FieldType.Double;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return FieldType.Timestamp;
        if (type == typeof(TimeSpan)) return FieldType.Duration;
        if (type == typeof(byte[])) return FieldType.Bytes;

        return FieldType.Object;
    }

    private static string StripAlias(string path, string alias) {
        var prefix = alias + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }
}
