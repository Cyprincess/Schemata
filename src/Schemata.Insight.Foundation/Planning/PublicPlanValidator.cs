using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Expressions.Skeleton;
using Schemata.Insight.Foundation.Drivers;
using Schemata.Insight.Foundation.Materialization;
using Schemata.Insight.Skeleton.Plan;

namespace Schemata.Insight.Foundation.Planning;

internal sealed class PublicPlanValidator(IServiceProvider services)
{
    internal void Validate(PlanNode plan) => Visit(plan, null);

    private Dictionary<string, Value> Visit(PlanNode node, Dictionary<string, Value>? nested) {
        switch (node) {
            case SourceNode source:
                return nested is not null ? new(nested, StringComparer.Ordinal) : new(StringComparer.Ordinal) {
                    [source.Alias] = source.Config.DriverName == RepositoryDriver.DriverName
                        ? new Typed(RepositoryDriver.Resolve(services, source.Config).PublicType) : new External(),
                };
            case LimitNode limit:
                return Visit(limit.Input, nested);
            case FilterNode filter: {
                var shape = Visit(filter.Input, nested);
                Analyze(filter.Predicate, shape, nested is null && TypedPrefix(filter.Input));
                return shape;
            }
            case OrderNode order: {
                var shape = Visit(order.Input, nested);
                foreach (var key in services.GetRequiredService<IOrderCompiler>().Parse(order.OrderBy)) Resolve(shape, new(key.Path));
                return shape;
            }
            case JoinNode join: {
                var shape = Visit(join.Left, nested);
                foreach (var pair in Visit(join.Right, nested)) shape.Add(pair.Key, pair.Value);
                Analyze(join.On, shape);
                return shape;
            }
            case ComputeNode compute: {
                var shape = Visit(compute.Input, nested);
                foreach (var field in compute.Fields) shape[field.Alias] = Analyze(field.Expression, shape);
                return shape;
            }
            case GroupNode group: {
                var input = Visit(group.Input, nested);
                var result = new Dictionary<string, Value>(StringComparer.Ordinal);
                foreach (var key in group.Keys) result[key.Split('.').Last()] = Resolve(input, new(key.Split('.')));
                foreach (var aggregation in group.Aggregations) {
                    if (aggregation.Field != "*" && !string.IsNullOrWhiteSpace(aggregation.Field)) Resolve(input, new(aggregation.Field.Split('.')));
                    result[aggregation.Alias] = new Scalar();
                }
                if (nested is not null) {
                    var alias = NestedAlias(group.Input);
                    result.TryGetValue(alias, out var bare);
                    result[alias] = new Qualified(bare, new Fields(new(result, StringComparer.Ordinal)));
                }
                return result;
            }
            case SelectionNode selection: {
                var input = Visit(selection.Input, nested);
                if (selection.Items.IsDefaultOrEmpty) return input;
                var result = new Dictionary<string, Value>(StringComparer.Ordinal);
                foreach (var item in selection.Items) {
                    if (item.Kind == SelectionKind.Expression) {
                        result[item.Alias] = Analyze(item.Expression!, input);
                        continue;
                    }
                    var value = Resolve(input, new(item.FieldPath!.Split('.')));
                    result[item.Alias] = value;
                    if (item.Nested is null) continue;
                    var environment = new Dictionary<string, Value>(input, StringComparer.Ordinal) {
                        [NestedAlias(item.Nested)] = Step(value, ExpressionPathMarker.Element, false),
                    };
                    Visit(item.Nested, environment);
                }
                return result;
            }
            default:
                throw Invalid("plan node");
        }
    }

    private Value Analyze(ParsedExpression expression, Dictionary<string, Value> shape, bool typed = false) {
        if (shape.Values.All(v => v is External)) return new External();
        var provider = services.GetKeyedService<IExpressionReferenceProvider>(expression.Language)
            ?? throw new InsightValidationException(InsightReasons.InvalidExpression, SchemataResources.INSIGHT_REFERENCE_PROVIDER_REQUIRED,
                new Dictionary<string, string?> { ["language"] = expression.Language });
        ExpressionReferences analysis;
        try { analysis = provider.Analyze(expression.Tree); }
        catch (ExpressionException error) { throw new InsightValidationException(InsightReasons.InvalidExpression,
            SchemataResources.INSIGHT_REFERENCE_ANALYSIS_FAILED, new Dictionary<string, string?> { ["language"] = expression.Language }, error); }
        foreach (var reference in analysis.References) {
            if (!typed && reference.TypeLiteral) continue;
            Resolve(shape, reference);
        }
        return Bind(analysis.Result, shape, typed);
    }

    private static Value Bind(ExpressionShape result, Dictionary<string, Value> environment, bool typed = false) => result switch {
        ExpressionShape.Scalar => new Scalar(),
        ExpressionShape.Null => new Null(),
        ExpressionShape.MapValues map => new MapValues(Bind(map.Value, environment, typed)),
        ExpressionShape.LiteralMap map => new LiteralMap(map.Fields.ToDictionary(p => p.Key, p => Bind(p.Value, environment, typed))),
        ExpressionShape.Concatenation concat => Concatenate(Bind(concat.Left, environment, typed), Bind(concat.Right, environment, typed)),
        ExpressionShape.Reference reference => !typed && reference.Access.TypeLiteral ? new Scalar() : Resolve(environment, reference.Access),
        ExpressionShape.Sequence sequence => new Sequence(sequence.Items.Select(v => Bind(v, environment, typed)).ToArray()),
        ExpressionShape.Map map => new Fields(map.Fields.ToDictionary(p => p.Key, p => Bind(p.Value, environment, typed), StringComparer.Ordinal)),
        ExpressionShape.Alternatives alternatives => new Alternatives(alternatives.Values.Select(v => Bind(v, environment, typed)).ToArray()),
        _ => throw Invalid("expression result"),
    };

    private static Value Concatenate(Value left, Value right) {
        if (IsSequence(left) && IsSequence(right)) return new Sequence([Step(left, ExpressionPathMarker.Element, false), Step(right, ExpressionPathMarker.Element, false)]);
        return new Scalar();
    }

    private static bool IsSequence(Value value) => value is Sequence || value is Typed typed && PublicModel.Element(typed.Type) is not null;

    private static bool TypedPrefix(PlanNode node) => node switch {
        SourceNode source => source.Config.DriverName == RepositoryDriver.DriverName,
        FilterNode filter => TypedPrefix(filter.Input),
        OrderNode order => TypedPrefix(order.Input),
        _ => false,
    };

    private static Value Resolve(Dictionary<string, Value> shape, ExpressionReference reference) {
        var path = reference.Path;
        if (path.Count == 0) throw Invalid("");
        if (path[0] is not string root) throw Invalid("root");
        var offset = 1;
        if (!shape.TryGetValue(root, out var value)) {
            if (reference.MayBeLiteral && shape.Values.All(v => !Declares(v, root))) return new Scalar();
            if (shape.Count != 1) throw Invalid(string.Join('.', path));
            value = shape.Values.Single();
            offset = 0;
            if (value is Typed typed && root == char.ToLowerInvariant(typed.Type.Name[0]) + typed.Type.Name[1..]) offset = 1;
        }
        for (var i = offset; i < path.Count; i++) value = Step(value, path[i], reference.Repeated);
        return value is Qualified qualified ? qualified.Bare ?? qualified.Members : value;
    }

    private static bool Declares(Value value, string name) => value switch {
        Typed typed => PublicModel.For(typed.Type).Declares(name)
            || name == char.ToLowerInvariant(typed.Type.Name[0]) + typed.Type.Name[1..],
        Fields fields => fields.Values.ContainsKey(name),
        Qualified qualified => Declares(qualified.Bare ?? qualified.Members, name),
        Alternatives alternatives => alternatives.Values.Any(v => Declares(v, name)),
        External => true,
        _ => false,
    };

    private static Value Step(Value value, object segment, bool repeated) {
        var text = Convert.ToString(segment, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        switch (value) {
            case Qualified qualified:
                return Step(qualified.Members, segment, repeated);
            case LiteralMap map:
                if (segment is ExpressionPathMarker.Iteration) return new Scalar();
                if (segment is ExpressionPathMarker.Element) return new Alternatives(map.Values.Values.ToArray());
                return map.Values.TryGetValue(segment, out var selected) ? selected : throw Invalid(text);
            case Null:
                return value;
            case MapValues map:
                return segment is ExpressionPathMarker.Iteration ? new Scalar() : map.Value;
            case External:
                return value;
            case Alternatives alternatives:
                return new Alternatives(alternatives.Values.Select(v => Step(v, segment, repeated)).ToArray());
            case Fields fields:
                if (segment is ExpressionPathMarker) return segment is ExpressionPathMarker.Iteration ? new Scalar() : new Alternatives(fields.Values.Values.ToArray());
                return segment is string key && fields.Values.TryGetValue(key, out var field) ? field : throw Invalid(text);
            case Sequence sequence:
                if (segment is ExpressionPathMarker || long.TryParse(text, out _)) return new Alternatives(sequence.Items);
                if (repeated) return new Alternatives(sequence.Items.Select(v => Step(v, segment, false)).ToArray());
                throw Invalid(text);
            case Typed typed: {
                var type = Nullable.GetUnderlyingType(typed.Type) ?? typed.Type;
                var map = PublicModel.MapValue(type);
                if (map is not null) return segment is ExpressionPathMarker.Iteration ? new Scalar() : new Typed(map);
                var element = PublicModel.Element(type);
                if (element is not null) {
                    if (segment is ExpressionPathMarker || long.TryParse(text, out _)) return new Typed(element);
                    if (repeated) return Step(new Typed(element), segment, false);
                    throw Invalid(text);
                }
                if (segment is not string member || type == typeof(object)) throw Invalid(text);
                return new Typed(PublicModel.For(type).Property(member).PropertyType);
            }
            default:
                throw Invalid(text);
        }
    }

    private static string NestedAlias(PlanNode node) => node switch {
        SourceNode source => source.Alias,
        FilterNode filter => NestedAlias(filter.Input),
        OrderNode order => NestedAlias(order.Input),
        ComputeNode compute => NestedAlias(compute.Input),
        GroupNode group => NestedAlias(group.Input),
        SelectionNode selection => NestedAlias(selection.Input),
        LimitNode limit => NestedAlias(limit.Input),
        _ => throw Invalid("nested"),
    };

    private static InsightValidationException Invalid(string path) => new(InsightReasons.InvalidArgument,
        SchemataResources.INSIGHT_PUBLIC_FIELD_INVALID, new Dictionary<string, string?> { ["field"] = path });
    private abstract record Value;
    private sealed record Typed(Type Type) : Value;
    private sealed record Scalar : Value;
    private sealed record Null : Value;
    private sealed record MapValues(Value Value) : Value;
    private sealed record LiteralMap(Dictionary<object, Value> Values) : Value;
    private sealed record External : Value;
    private sealed record Fields(Dictionary<string, Value> Values) : Value;
    private sealed record Qualified(Value? Bare, Fields Members) : Value;
    private sealed record Sequence(IReadOnlyList<Value> Items) : Value;
    private sealed record Alternatives(IReadOnlyList<Value> Values) : Value;
}
