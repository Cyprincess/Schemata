using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Plan;

namespace Schemata.Insight.Foundation.Materialization;

public static class SchemaBuilder
{
    public static IReadOnlyList<FieldDescriptor> For(
        Type                          entityType,
        ImmutableArray<SelectionItem> items,
        string                        alias
    ) {
        if (items.IsDefaultOrEmpty) {
            var fields = new List<FieldDescriptor>();
            foreach (var (name, property) in PublicModel.For(entityType).Properties) {
                fields.Add(Describe(name, property.PropertyType, alias, new()));
            }

            return fields;
        }

        var selected = new List<FieldDescriptor>(items.Length);
        foreach (var item in items) {
            switch (item.Kind) {
                case SelectionKind.Field when !string.IsNullOrWhiteSpace(item.FieldPath):
                    selected.Add(Describe(item.Alias, ResolveType(entityType, StripAlias(item.FieldPath, alias)), alias, new()));
                    break;
                case SelectionKind.Expression:
                    selected.Add(new(item.Alias, FieldType.Dynamic, null, false, []));
                    break;
                case SelectionKind.Nested when !string.IsNullOrWhiteSpace(item.FieldPath):
                    selected.Add(NestedDescriptor(entityType, item, alias));
                    break;
            }
        }

        return selected;
    }

    private static FieldDescriptor NestedDescriptor(Type parentType, SelectionItem item, string alias) {
        var type = ResolveType(parentType, StripAlias(item.FieldPath!, alias));
        var childType = PublicModel.Element(type);
        var childAlias = item.Nested is null ? item.Alias : SourceAlias(item.Nested);
        var children = childType is null ? [] : item.Nested is null
            ? For(childType, item.Children, childAlias)
            : Transform(For(childType, [], childAlias), Stages(item.Nested));
        return new(item.Alias, FieldType.Object, null, childType is not null, [..children]);
    }

    private static string SourceAlias(PlanNode node) => node switch {
        SourceNode source => source.Alias,
        FilterNode filter => SourceAlias(filter.Input),
        OrderNode order => SourceAlias(order.Input),
        ComputeNode compute => SourceAlias(compute.Input),
        GroupNode group => SourceAlias(group.Input),
        SelectionNode selection => SourceAlias(selection.Input),
        LimitNode limit => SourceAlias(limit.Input),
        _ => throw new InvalidOperationException("Nested selection requires one source."),
    };


    private static Type ResolveType(Type type, string path) {
        return PublicModel.For(type).Resolve(path);
    }

    private static FieldDescriptor Describe(string name, Type type, string? alias, HashSet<Type> active) {
        type = Nullable.GetUnderlyingType(type) ?? type;
        var scalar = InsightValueModel.ScalarType(type);
        if (scalar != FieldType.Object) return new(name, scalar, alias, false, []);
        if (!active.Add(type)) return new(name, FieldType.Object, alias, false, []);
        try {
            if (PublicModel.MapValue(type) is { } valueType)
                return new(name, FieldType.Map, alias, false, [Describe("*", valueType, alias, active)]);
            if (PublicModel.Element(type) is { } element) {
                var descriptor = Describe(name, element, alias, active);
                return new(name, descriptor.Type, alias, true, descriptor.Children) { Element = descriptor };
            }
            if (type == typeof(object)) throw InsightValueModel.Unsupported(type);
            var children = ImmutableArray.CreateBuilder<FieldDescriptor>();
            foreach (var (child, property) in PublicModel.For(type).Properties) children.Add(Describe(child, property.PropertyType, alias, active));
            return new(name, FieldType.Object, alias, false, children.ToImmutable());
        } finally { active.Remove(type); }
    }

    public static QueryInsightResponse Complete(QueryInsightResponse response) {
        var fields = new Dictionary<string, FieldDescriptor>(StringComparer.Ordinal);
        foreach (var field in response.Schema) fields.Add(field.Name, field);
        var changed = false;
        for (var i = 0; i < response.Rows.Count; i++) {
            var row = RowMaterializer.NormalizeRow(response.Rows[i], response.Schema);
            response.Rows[i] = row;
            foreach (var (name, value) in row) {
                fields.TryGetValue(name, out var field);
                var completed = CompleteField(field ?? new(name, FieldType.Unspecified, null, false, []), value);
                if (!ReferenceEquals(field, completed)) changed = true;
                fields[name] = completed;
            }
        }
        if (changed) response.Schema = [..fields.Values];
        return response;
    }

    private static FieldDescriptor DescribeValue(string name, object value, string? alias) {
        var scalar = InsightValueModel.ScalarType(value.GetType());
        if (scalar != FieldType.Object) return new(name, scalar, alias, false, []);
        if (value is IReadOnlyDictionary<string, object?> map) {
            var children = ImmutableArray.CreateBuilder<FieldDescriptor>();
            foreach (var (key, child) in map) children.Add(child is null ? new(key, FieldType.Unspecified, alias, false, []) : DescribeValue(key, child, alias));
            return new(name, FieldType.Object, alias, false, children.ToImmutable());
        }
        if (value is System.Collections.IEnumerable items) {
            var element = new FieldDescriptor(name, FieldType.Unspecified, alias, false, []);
            foreach (var item in items) element = CompleteField(element, item);
            return new(name, element.Type, alias, true, element.Children) { Element = element };
        }
        throw InsightValueModel.Unsupported(value.GetType());
    }

    private static IReadOnlyList<PlanNode> Stages(PlanNode node) {
        var stages = new List<PlanNode>();
        while (node is not SourceNode) {
            stages.Add(node);
            node = node switch {
                FilterNode filter => filter.Input,
                OrderNode order => order.Input,
                ComputeNode compute => compute.Input,
                GroupNode group => group.Input,
                SelectionNode selection => selection.Input,
                LimitNode limit => limit.Input,
                _ => throw new InvalidOperationException("Nested selection requires one source."),
            };
        }
        stages.Reverse();
        return stages;
    }

    private static FieldDescriptor CompleteField(FieldDescriptor field, object? value) {
        if (value is null) return field;
        if (field.Type == FieldType.Dynamic && !field.IsList) {
            ValidateDynamic(value);
            return field;
        }
        if (field.Type == FieldType.Unspecified || field.Type == FieldType.Object && field.Children.IsDefaultOrEmpty && !field.IsList)
            field = DescribeValue(field.Name, value, field.SourceAlias);
        if (field.IsList && value is System.Collections.IEnumerable items && value is not string && value is not byte[]) {
            var original = field.Element ?? field with { IsList = false };
            var element = original;
            foreach (var item in items) element = CompleteField(element, item);
            return ReferenceEquals(element, original) ? field : field with { Type = element.Type, Children = element.Children, Element = element };
        }
        if (value is IReadOnlyDictionary<string, object?> map) {
            if (field.IsList || field.Type is not (FieldType.Map or FieldType.Object)) throw InsightValueModel.Unsupported(value.GetType());
            var children = field.Children;
            var changed = false;
            for (var i = 0; i < children.Length; i++) {
                var child = children[i];
                if (field.Type == FieldType.Map && child.Name == "*") {
                    foreach (var item in map.Values) child = CompleteField(child, item);
                } else if (map.TryGetValue(child.Name, out var item)) child = CompleteField(child, item);
                if (!ReferenceEquals(child, children[i])) {
                    children = children.SetItem(i, child);
                    changed = true;
                }
            }
            if (field.Type == FieldType.Object) {
                foreach (var (name, item) in map) {
                    var found = false;
                    foreach (var child in children) if (child.Name == name) { found = true; break; }
                    if (found) continue;
                    children = children.Add(item is null ? new(name, FieldType.Unspecified, field.SourceAlias, false, []) : DescribeValue(name, item, field.SourceAlias));
                    changed = true;
                }
            }
            return changed ? field with { Children = children } : field;
        }
        if (field.IsList || InsightValueModel.ScalarType(value.GetType()) != field.Type)
            throw InsightValueModel.Unsupported(value.GetType());
        return field;
    }

    private static void ValidateDynamic(object? value) {
        if (value is null || InsightValueModel.ScalarType(value.GetType()) != FieldType.Object) return;
        if (value is IReadOnlyDictionary<string, object?> map) {
            foreach (var child in map.Values) ValidateDynamic(child);
        } else if (value is System.Collections.IEnumerable items) {
            foreach (var child in items) ValidateDynamic(child);
        } else throw InsightValueModel.Unsupported(value.GetType());
    }

    private static string StripAlias(string path, string alias) {
        var prefix = alias + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }
    public static ImmutableArray<FieldDescriptor> Transform(
        IReadOnlyList<FieldDescriptor> driverSchema,
        IReadOnlyList<PlanNode>        localStages
    ) {
        var schema = ImmutableArray.CreateRange(driverSchema);
        foreach (var stage in localStages) {
            if (stage is GroupNode group) {
                var grouped = ImmutableArray.CreateBuilder<FieldDescriptor>(group.Keys.Length + group.Aggregations.Length);
                foreach (var key in group.Keys) {
                    grouped.Add(FieldFor(new(LastSegment(key), SelectionKind.Field, key, null, [], null), schema, group.SourceSet));
                }
                foreach (var aggregation in group.Aggregations) {
                    var type = aggregation.Function is AggregationFunction.Count or AggregationFunction.CountDistinct
                        ? FieldType.Int64 : aggregation.Function is AggregationFunction.Sum or AggregationFunction.Avg
                            ? FieldType.Double : FieldType.Dynamic;
                    var descriptor = new FieldDescriptor(aggregation.Alias, type, null, false, []);
                    var index = -1;
                    for (var i = 0; i < grouped.Count; i++) if (grouped[i].Name == aggregation.Alias) { index = i; break; }
                    if (index < 0) grouped.Add(descriptor); else grouped[index] = descriptor;
                }
                schema = grouped.ToImmutable();
            } else if (stage is ComputeNode compute) {
                var computed = schema.ToBuilder();
                foreach (var field in compute.Fields) {
                    var descriptor = new FieldDescriptor(field.Alias, FieldType.Dynamic, null, false, []);
                    var index = -1;
                    for (var i = 0; i < computed.Count; i++) if (computed[i].Name == field.Alias) { index = i; break; }
                    if (index < 0) computed.Add(descriptor); else computed[index] = descriptor;
                }
                schema = computed.ToImmutable();
            } else if (stage is SelectionNode { Items.IsDefaultOrEmpty: false } selection) {
                var fields = ImmutableArray.CreateBuilder<FieldDescriptor>(selection.Items.Length);
                AddSelectionFields(selection.Items, false, schema, fields, selection.SourceSet);
                AddSelectionFields(selection.Items, true, schema, fields, selection.SourceSet);
                schema = fields.ToImmutable();
            }
        }
        return schema;
    }

    private static void AddSelectionFields(
        ImmutableArray<SelectionItem>                   items,
        bool                                            expressions,
        IReadOnlyList<FieldDescriptor>                  driverSchema,
        ImmutableArray<FieldDescriptor>.Builder         fields,
        IReadOnlySet<string> aliases
    ) {
        foreach (var item in items) {
            if ((item.Kind is SelectionKind.Expression) != expressions) {
                continue;
            }

            fields.Add(FieldFor(item, driverSchema, aliases));
        }
    }

    private static FieldDescriptor FieldFor(SelectionItem item, IReadOnlyList<FieldDescriptor> driverSchema, IReadOnlySet<string> aliases) {
        if (item.Kind is SelectionKind.Expression) {
            return new(item.Alias, FieldType.Dynamic, null, false, []);
        }

        var path = item.FieldPath?.Split('.', StringSplitOptions.RemoveEmptyEntries) ?? [];
        IReadOnlyList<FieldDescriptor> fields = driverSchema;
        FieldDescriptor? selected = null;
        var first = path.Length > 1 && aliases.Contains(path[0]) ? 1 : 0;
        for (var offset = first; offset < path.Length; offset++) {
            selected = null;
            foreach (var field in fields) {
                if (field.Name == path[offset]) { selected = field; break; }
            }
            if (selected is null) break;
            fields = selected.Children;
        }
        if (selected is not null) {
            if (item.Kind == SelectionKind.Nested && item.Nested is not null) {
                selected = selected with { Children = Transform(selected.Children, Stages(item.Nested)), Element = null };
            }
            return selected with { Name = item.Alias };
        }
        return new(item.Alias, FieldType.Dynamic, null, false, []);
    }

    private static string LastSegment(string path) {
        var index = path.LastIndexOf('.');
        return index < 0 ? path : path[(index + 1)..];
    }
}
