using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Parlot;
using Schemata.Common;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Aip.Values;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

internal sealed class AipCompileVisitor
{
    private readonly ExpressionCompileOptions? _options;

    public AipCompileVisitor(Type contextType, ExpressionCompileOptions? options) {
        _options  = options;
        Parameter = Expression.Parameter(contextType, LowerFirst(contextType.Name));
    }

    public ParameterExpression Parameter { get; }

    public Expression Visit(Filter node) { return Combine(node.Sequences.Select(Visit), Expression.AndAlso); }

    public Expression Visit(Sequence node) { return Combine(node.Factors.Select(Visit), Expression.AndAlso); }

    public Expression Visit(Factor node) { return Combine(node.Terms.Select(Visit), Expression.OrElse); }

    public Expression Visit(Term node) {
        var expression = Visit(node.Simple);
        return node.Modifier is null ? expression : Expression.Not(ToBoolean(expression));
    }

    public Expression Visit(ISimple node) {
        return node switch {
            Restriction restriction => Visit(restriction),
            Filter filter           => Visit(filter),
            var _                   => throw new ParseException("Unsupported AIP simple expression.", node.Position),
        };
    }

    public Expression Visit(Restriction node) {
        var left = Visit(node.Comparable);
        if (node.Comparator is null || node.Arg is null) {
            return ToBoolean(left);
        }

        if (node.Comparator is Equal && TryGetQuotedLiteral(node.Arg, out var literal)) {
            return Expression.Equal(left, ConvertIfNeeded(Expression.Constant(literal, typeof(string)), left.Type));
        }

        var right = Visit(node.Arg);
        return BuildBinary(node.Comparator, left, right);
    }

    private static bool TryGetQuotedLiteral(IArg arg, out string literal) {
        if (arg is Member { Value: Text { IsQuoted: true } text, Fields.Count: 0 }) {
            literal = text.Value;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    public Expression Visit(IArg node) {
        return node switch {
            IComparableArg comparable => Visit(comparable),
            Filter filter             => Visit(filter),
            var _                     => throw new ParseException("Unsupported AIP argument.", node.Position),
        };
    }

    public Expression Visit(IComparableArg node) {
        return node switch {
            Member member     => Visit(member),
            Function function => Visit(function),
            var _             => throw new ParseException("Unsupported AIP comparable expression.", node.Position),
        };
    }

    public Expression Visit(Member node) {
        var expression = VisitValue(node.Value, true);
        foreach (var field in node.Fields) {
            expression = Access(expression, field);
        }

        return expression;
    }

    public Expression Visit(Function node) {
        var names = GetSegments(node.Member);
        var name  = string.Join(".", names);
        var function = AipBuiltInFunctions.Resolve(name, _options)
                    ?? AipBuiltInFunctions.Resolve(names.LastOrDefault() ?? string.Empty, _options);
        if (function is null) {
            throw new ParseException($"Unknown AIP function '{name}'.", node.Position);
        }

        return function.Build(node.Args.Select(Visit).ToArray());
    }

    private Expression VisitValue(IValue node, bool preferMember) {
        return node switch {
            Text text when preferMember && TryResolveIdentifier(text.Value, out var access) => access,
            Text text => Expression.Constant(text.Value),
            Integer integer => Expression.Constant(integer.Value),
            Number number => Expression.Constant(number.Value),
            Truth truth => Expression.Constant(truth.Value),
            Null => Expression.Constant(null),
            var _ => throw new ParseException("Unsupported AIP value.", node.Position),
        };
    }

    private bool TryResolveIdentifier(string name, out Expression expression) {
        if (string.Equals(name, Parameter.Name, StringComparison.Ordinal)) {
            expression = Parameter;
            return true;
        }

        return TryAccess(Parameter, name, out expression);
    }

    private Expression Access(Expression source, IField field) {
        var key = field switch {
            Text text       => text.Value,
            Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            var _           => throw new ParseException("Unsupported AIP field.", field.Position),
        };

        if (typeof(IDictionary).IsAssignableFrom(source.Type)) {
            return Expression.Property(source, "Item", Expression.Constant(key));
        }

        if (source.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(source.Type)) {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) {
                throw new ParseException("Expected collection index.", field.Position);
            }

            var elementType = source.Type.GetElementType()
                           ?? source.Type.GenericTypeArguments.FirstOrDefault() ?? typeof(object);
            var method = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                           .Single(m => m.Name == nameof(Enumerable.ElementAt)
                                                     && m.GetParameters().Length == 2)
                                           .MakeGenericMethod(elementType);
            return Expression.Call(method, source, Expression.Constant(index));
        }

        if (TryAccess(source, key, out var access)) {
            return access;
        }

        throw new ParseException($"Unknown field '{key}'.", field.Position);
    }

    private static bool TryAccess(Expression source, string name, out Expression expression) {
        var memberName = SchemataNaming.ToClrMemberName(name);
        var member = source.Type.GetMember(memberName, BindingFlags.Instance | BindingFlags.Public).FirstOrDefault();
        if (member is PropertyInfo property) {
            expression = Expression.Property(source, property);
            return true;
        }

        if (member is FieldInfo field) {
            expression = Expression.Field(source, field);
            return true;
        }

        expression = null!;
        return false;
    }

    private static IReadOnlyList<string> GetSegments(Member member) {
        var segments = new List<string>();
        if (member.Value is Text text) {
            segments.Add(text.Value);
        }

        foreach (var field in member.Fields) {
            if (field is Text segment) {
                segments.Add(segment.Value);
            }
        }

        return segments;
    }

    private static string LowerFirst(string name) {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    private static Expression BuildBinary(IBinary op, Expression left, Expression right) {
        if (op is Has) {
            return BuildHas(left, right);
        }

        if (op is Equal) {
            return BuildEqual(left, right);
        }

        return Expression.MakeBinary(op.Type!.Value, left, ConvertIfNeeded(right, left.Type));
    }

    private static Expression BuildEqual(Expression left, Expression right) {
        // AIP-160 wildcard equality only applies to string literals containing '*'.
        // Other types and quoted strings without '*' fall through to plain equality.
        if (left.Type != typeof(string) || right is not ConstantExpression { Value: string pattern }) {
            return Expression.Equal(left, ConvertIfNeeded(right, left.Type));
        }

        var (literal, hasWildcard, leading, trailing) = AnalyzeWildcardPattern(pattern);

        if (!hasWildcard) {
            return Expression.Equal(left, Expression.Constant(literal, typeof(string)));
        }

        if (literal.Length == 0) {
            // Bare "*" / "**" — match any non-null value.
            return Expression.NotEqual(left, Expression.Constant(null, typeof(string)));
        }

        var literalExpr = Expression.Constant(literal, typeof(string));

        return (leading, trailing) switch {
            (false, true) => Expression.Call(left, nameof(string.StartsWith), null, literalExpr),
            (true, false) => Expression.Call(left, nameof(string.EndsWith), null, literalExpr),
            (true, true)  => Expression.Call(left, nameof(string.Contains), null, literalExpr),
            // Unsupported pattern shape (e.g. "A*B"): fall back to literal equality after stripping
            // wildcards so the result is well-defined rather than throwing at compile time.
            var _ => Expression.Equal(left, literalExpr),
        };
    }

    private static (string Literal, bool HasWildcard, bool Leading, bool Trailing) AnalyzeWildcardPattern(
        string pattern
    ) {
        if (pattern.IndexOf('*') < 0) {
            return (pattern, false, false, false);
        }

        var leading  = pattern.Length > 0 && pattern[0] == '*';
        var trailing = pattern.Length > 0 && pattern[pattern.Length - 1] == '*';

        var start = leading ? 1 : 0;
        var end   = trailing ? pattern.Length - 1 : pattern.Length;
        var inner = start < end ? pattern.Substring(start, end - start) : string.Empty;

        // Trim any extra leading/trailing wildcards that don't affect semantics: "**foo" ≡ "*foo".
        while (inner.Length > 0 && inner[0] == '*') {
            inner = inner.Substring(1);
        }

        while (inner.Length > 0 && inner[inner.Length - 1] == '*') {
            inner = inner.Substring(0, inner.Length - 1);
        }

        // Reject inner wildcards — "A*B" is not a supported AIP-160 simple wildcard.
        // Returning HasWildcard=true with Leading=Trailing=false signals the caller to
        // fall back to literal equality on the cleaned pattern.
        if (inner.IndexOf('*') >= 0) {
            return (inner, true, false, false);
        }

        return (inner, true, leading, trailing);
    }

    private static Expression BuildHas(Expression left, Expression right) {
        if (right is ConstantExpression { Value: "*" }) {
            return left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) is null
                ? Expression.Constant(true)
                : Expression.NotEqual(left, Expression.Constant(null, left.Type));
        }

        if (typeof(IDictionary).IsAssignableFrom(left.Type)) {
            var method = left.Type.GetMethod("ContainsKey")
                      ?? typeof(IDictionary).GetMethod("Contains", [typeof(object)])!;
            var parameterType = method.GetParameters()[0].ParameterType;
            return Expression.Call(left, method, ConvertIfNeeded(right, parameterType));
        }

        if (left.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(left.Type)) {
            var elementType = left.Type.GetElementType()
                           ?? left.Type.GenericTypeArguments.FirstOrDefault() ?? typeof(object);
            var method = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                           .Single(m => m.Name == nameof(Enumerable.Contains)
                                                     && m.GetParameters().Length == 2)
                                           .MakeGenericMethod(elementType);
            return Expression.Call(method, left, ConvertIfNeeded(right, elementType));
        }

        if (left.Type == typeof(string)) {
            return Expression.Call(left, nameof(string.Contains), null, ConvertIfNeeded(right, typeof(string)));
        }

        return Expression.Equal(left, ConvertIfNeeded(right, left.Type));
    }

    private static Expression ConvertIfNeeded(Expression expression, Type type) {
        return expression.Type == type ? expression : Expression.Convert(expression, type);
    }

    private static Expression ToBoolean(Expression expression) {
        if (expression.Type == typeof(bool)) {
            return expression;
        }

        if (expression.Type.IsValueType && Nullable.GetUnderlyingType(expression.Type) is null) {
            return Expression.Constant(true);
        }

        return Expression.NotEqual(expression, Expression.Constant(null, expression.Type));
    }

    private static Expression Combine(
        IEnumerable<Expression>                        expressions,
        Func<Expression, Expression, BinaryExpression> merge
    ) {
        return expressions.Aggregate(merge);
    }
}
