using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

public sealed class CelReferenceProvider : IExpressionReferenceProvider
{
    public ExpressionReferences Analyze(IExpressionTree tree) {
        if (tree is not CelNode node) throw new ExpressionException("Expected a CEL expression.");
        var references = new List<ExpressionReference>();
        var result = Visit(node, new Dictionary<string, ExpressionShape>(StringComparer.Ordinal), references);
        return new(references, result);
    }

    private static ExpressionShape Visit(CelNode node, Dictionary<string, ExpressionShape> scope, List<ExpressionReference> references) {
        switch (node) {
            case CelConstant { Value: null }:
                return new ExpressionShape.Null();
            case CelConstant:
                return new ExpressionShape.Scalar();
            case CelIdentifier identifier: {
                var value = scope.TryGetValue(identifier.Name, out var bound) ? bound
                    : new ExpressionShape.Reference(new([identifier.Name], TypeLiteral: identifier.Name is "bool" or "bytes" or "double" or "int" or "list" or "map" or "null_type" or "string" or "type" or "uint"));
                Record(value, references);
                return value;
            }
            case CelMember member:
                if (!scope.ContainsKey("google") && member is { Target: CelMember { Target: CelIdentifier { Name: "google" }, Member: "protobuf" }, Member: "Timestamp" or "Duration" }) {
                    var type = new ExpressionReference(["google", "protobuf", member.Member], TypeLiteral: true);
                    references.Add(type);
                    return new ExpressionShape.Reference(type);
                }
                return Access(Visit(member.Target, scope, references), member.Member, false, references);
            case CelIndex index: {
                var target = Visit(index.Target, scope, references);
                Visit(index.Index, scope, references);
                var key = index.Index is CelConstant constant ? constant.Value : null;
                return Access(target, key ?? "", index.Index is not CelConstant, references);
            }
            case CelConditional conditional:
                Visit(conditional.Condition, scope, references);
                return new ExpressionShape.Alternatives([Visit(conditional.WhenTrue, scope, references), Visit(conditional.WhenFalse, scope, references)]);
            case CelList list:
                return new ExpressionShape.Sequence(list.Items.Select(item => Visit(item, scope, references)).ToArray());
            case CelMap map: {
                var fields = new Dictionary<object, ExpressionShape>();
                var dynamicValues = new List<ExpressionShape>();
                var dynamicKeys = false;
                foreach (var entry in map.Entries) {
                    Visit(entry.Key, scope, references);
                    var value = Visit(entry.Value, scope, references);
                    var key = entry.Key is CelConstant literal ? literal.Value : null;
                    dynamicValues.Add(value);
                    if (key is null) dynamicKeys = true;
                    else fields[key] = value;
                }
                return dynamicKeys ? new ExpressionShape.MapValues(new ExpressionShape.Alternatives(dynamicValues)) : new ExpressionShape.LiteralMap(fields);
            }
            case CelUnary unary:
                Visit(unary.Operand, scope, references);
                return new ExpressionShape.Scalar();
            case CelBinary binary: {
                var left = Visit(binary.Left, scope, references);
                var right = Visit(binary.Right, scope, references);
                if (binary.Operator == "+") return new ExpressionShape.Concatenation(left, right);
                return new ExpressionShape.Scalar();
            }
            case CelCall call: {
                var values = call.Args.Select(argument => Visit(argument, scope, references)).ToArray();
                return call.Name == "dyn" && values.Length == 1 ? values[0] : new ExpressionShape.Scalar();
            }
            case CelMemberCall call:
                return MemberCall(call, scope, references);
            default:
                throw new ExpressionException("Unsupported CEL reference node.");
        }
    }

    private static ExpressionShape MemberCall(CelMemberCall call, Dictionary<string, ExpressionShape> scope, List<ExpressionReference> references) {
        var target = Visit(call.Target, scope, references);
        if (call.Name is not ("map" or "filter" or "exists" or "all" or "exists_one" or "existsOne" or "transformList" or "transformMap")) {
            foreach (var argument in call.Args) Visit(argument, scope, references);
            return new ExpressionShape.Scalar();
        }
        if (call.Args.Count < 2 || call.Args[0] is not CelIdentifier first) throw new ExpressionException("Invalid CEL macro binding.");
        var inner = new Dictionary<string, ExpressionShape>(scope, StringComparer.Ordinal);
        var two = call.Args.Count >= 3 && call.Args[1] is CelIdentifier;
        var element = Iteration(target, false, references);
        inner[first.Name] = Iteration(target, true, references);
        var start = 1;
        if (two) {
            inner[first.Name] = new ExpressionShape.Scalar();
            inner[((CelIdentifier)call.Args[1]).Name] = element;
            start = 2;
        }
        ExpressionShape result = new ExpressionShape.Scalar();
        for (var i = start; i < call.Args.Count; i++) result = Visit(call.Args[i], inner, references);
        return call.Name switch {
            "filter" => new ExpressionShape.Sequence([Iteration(target, true, references)]),
            "map" or "transformList" => new ExpressionShape.Sequence([result]),
            "transformMap" => new ExpressionShape.MapValues(result),
            _ => new ExpressionShape.Scalar(),
        };
    }

    private static ExpressionShape Iteration(ExpressionShape shape, bool keysForMap, List<ExpressionReference> references) => shape switch {
        ExpressionShape.Reference reference => Extend(reference.Access, keysForMap ? ExpressionPathMarker.Iteration : ExpressionPathMarker.Element, true, references),
        ExpressionShape.Sequence sequence => new ExpressionShape.Alternatives(sequence.Items),
        ExpressionShape.Concatenation concat => new ExpressionShape.Alternatives([Iteration(concat.Left, keysForMap, references), Iteration(concat.Right, keysForMap, references)]),
        ExpressionShape.LiteralMap map => keysForMap ? new ExpressionShape.Scalar() : new ExpressionShape.Alternatives(map.Fields.Values.ToArray()),
        ExpressionShape.Map map => keysForMap ? new ExpressionShape.Scalar() : new ExpressionShape.Alternatives(map.Fields.Values.ToArray()),
        ExpressionShape.MapValues map => keysForMap ? new ExpressionShape.Scalar() : map.Value,
        ExpressionShape.Alternatives alternatives => new ExpressionShape.Alternatives(alternatives.Values.Select(v => Iteration(v, keysForMap, references)).ToArray()),
        _ => throw new ExpressionException("CEL macro receiver must have a collection shape."),
    };

    private static ExpressionShape Access(ExpressionShape shape, object member, bool dynamic, List<ExpressionReference> references) {
        switch (shape) {
            case ExpressionShape.Concatenation concat:
                return new ExpressionShape.Alternatives([Iteration(concat.Left, false, references), Iteration(concat.Right, false, references)]);
            case ExpressionShape.LiteralMap map:
                if (!dynamic && map.Fields.TryGetValue(member, out var selected)) return selected;
                return new ExpressionShape.Alternatives(map.Fields.Values.ToArray());
            case ExpressionShape.Null:
                return shape;
            case ExpressionShape.MapValues map:
                return map.Value;
            case ExpressionShape.Reference reference:
                return Extend(reference.Access, dynamic ? ExpressionPathMarker.Element : member, dynamic, references);
            case ExpressionShape.Alternatives alternatives:
                return new ExpressionShape.Alternatives(alternatives.Values.Select(v => Access(v, member, dynamic, references)).ToArray());
            case ExpressionShape.Sequence sequence:
                if (!dynamic && int.TryParse(Convert.ToString(member, CultureInfo.InvariantCulture), out var index) && index >= 0 && index < sequence.Items.Count) return sequence.Items[index];
                return new ExpressionShape.Alternatives(sequence.Items);
            case ExpressionShape.Map map:
                if (!dynamic && member is string key && map.Fields.TryGetValue(key, out var value)) return value;
                return new ExpressionShape.Alternatives(map.Fields.Values.ToArray());
            default:
                throw new ExpressionException("Cannot access a structural member of a scalar expression.");
        }
    }

    private static ExpressionShape Extend(ExpressionReference reference, object segment, bool dynamic, List<ExpressionReference> references) {
        var path = reference.Path.Append(segment).ToArray();
        var extended = new ExpressionReference(path, reference.DynamicIndex || dynamic);
        references.Add(extended);
        return new ExpressionShape.Reference(extended);
    }

    private static void Record(ExpressionShape shape, List<ExpressionReference> references) {
        if (shape is ExpressionShape.Reference reference) references.Add(reference.Access);
        else if (shape is ExpressionShape.Alternatives alternatives) foreach (var value in alternatives.Values) Record(value, references);
    }
}
