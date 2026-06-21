using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Parlot;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Aip.Values;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Converts AIP filter AST nodes into LINQ expression tree nodes.
/// </summary>
internal sealed class AipCompileVisitor
{
    private static readonly MethodInfo MemberMethod         = Method(nameof(DynamicValues.Member));
    private static readonly MethodInfo TruthyMethod         = Method(nameof(DynamicValues.Truthy));
    private static readonly MethodInfo EqualMethod          = Method(nameof(DynamicValues.Equal));
    private static readonly MethodInfo NotEqualMethod       = Method(nameof(DynamicValues.NotEqual));
    private static readonly MethodInfo LessMethod           = Method(nameof(DynamicValues.Less));
    private static readonly MethodInfo LessOrEqualMethod    = Method(nameof(DynamicValues.LessOrEqual));
    private static readonly MethodInfo GreaterMethod        = Method(nameof(DynamicValues.Greater));
    private static readonly MethodInfo GreaterOrEqualMethod = Method(nameof(DynamicValues.GreaterOrEqual));

    private readonly ExpressionCompileOptions? _options;

    // Compiling against a dictionary context evaluates member access and operators through
    // DynamicValues at run time instead of binding statically-typed members.
    private readonly bool _dynamic;

    private Expression? _guard;

    /// <summary>
    ///     Creates a visitor for expressions evaluated against the supplied context type.
    /// </summary>
    public AipCompileVisitor(Type contextType, ExpressionCompileOptions? options) {
        _options  = options;
        _dynamic  = typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(contextType);
        Parameter = Expression.Parameter(contextType, LowerFirst(contextType.Name));
    }

    /// <summary>
    ///     Gets the root parameter used by generated expressions.
    /// </summary>
    public ParameterExpression Parameter { get; }

    /// <summary>
    ///     Visits a complete AIP filter.
    /// </summary>
    public Expression Visit(Filter node) { return Combine(node.Sequences.Select(Visit), Expression.AndAlso); }

    /// <summary>
    ///     Visits an AND sequence in an AIP filter.
    /// </summary>
    public Expression Visit(Sequence node) { return Combine(node.Factors.Select(Visit), Expression.AndAlso); }

    /// <summary>
    ///     Visits an OR factor in an AIP filter.
    /// </summary>
    public Expression Visit(Factor node) { return Combine(node.Terms.Select(Visit), Expression.OrElse); }

    /// <summary>
    ///     Visits a possibly negated AIP term.
    /// </summary>
    public Expression Visit(Term node) {
        var expression = Visit(node.Simple);
        if (node.Modifier is null) {
            return expression;
        }

        return Expression.Not(_dynamic ? expression : ToBoolean(expression));
    }

    /// <summary>
    ///     Visits a simple AIP expression.
    /// </summary>
    public Expression Visit(ISimple node) {
        return node switch {
            Restriction restriction => Visit(restriction),
            Filter filter           => Visit(filter),
            var _                   => throw new ParseException("Unsupported AIP simple expression.", node.Position),
        };
    }

    /// <summary>
    ///     Visits an AIP restriction and applies null-chain guards when needed.
    /// </summary>
    public Expression Visit(Restriction node) {
        if (_dynamic) {
            return VisitDynamic(node);
        }

        var outerGuard = _guard;
        _guard = null;

        Expression result;
        if (node.Comparator is Has && node.Arg is not null && TryBuildRepeatedHas(node.Comparable, node.Arg, out var has)) {
            result = has;
        } else {
            var left = Visit(node.Comparable);
            if (node.Comparator is null || node.Arg is null) {
                // A bare term must name a field. An unresolved identifier surfaces here as a string
                // constant, which would otherwise compile to a vacuous `!= null` matching every row.
                if (left is ConstantExpression { Value: string token }) {
                    throw new ParseException($"Unknown field '{token}'.", node.Position);
                }

                result = ToBoolean(left);
            } else if (node.Comparator is Equal && TryGetQuotedLiteral(node.Arg, out var literal)) {
                result = Expression.Equal(left, ConvertIfNeeded(Expression.Constant(literal, typeof(string)), left.Type));
            } else {
                var right = Visit(node.Arg);
                result = BuildBinary(node.Comparator, left, right);
            }
        }

        // AIP-160 skip-as-nonmatch: a comparison against a value reached through a null
        // chain (e.g. advisor.age == 0 when advisor is null) must evaluate to false even
        // when the leaf's default value would coincidentally match the literal.
        if (_guard is not null) {
            result = Expression.AndAlso(_guard, result);
        }

        _guard = outerGuard;
        return result;
    }

    private static bool TryGetQuotedLiteral(IArg arg, out string literal) {
        if (arg is Member { Value: Text { IsQuoted: true } text, Fields.Count: 0 }) {
            literal = text.Value;
            return true;
        }

        literal = string.Empty;
        return false;
    }

    /// <summary>
    ///     Visits an AIP function or comparison argument.
    /// </summary>
    public Expression Visit(IArg node) {
        return node switch {
            IComparableArg comparable => Visit(comparable),
            Filter filter             => Visit(filter),
            var _                     => throw new ParseException("Unsupported AIP argument.", node.Position),
        };
    }

    /// <summary>
    ///     Visits an AIP argument that can appear on the left side of a comparator.
    /// </summary>
    public Expression Visit(IComparableArg node) {
        return node switch {
            Member member     => Visit(member),
            Function function => Visit(function),
            var _             => throw new ParseException("Unsupported AIP comparable expression.", node.Position),
        };
    }

    /// <summary>
    ///     Visits an AIP member path or literal value.
    /// </summary>
    public Expression Visit(Member node) {
        if (_dynamic) {
            return VisitDynamic(node);
        }

        var expression = VisitValue(node.Value, true);
        foreach (var field in node.Fields) {
            expression = Access(expression, field);
        }

        return expression;
    }

    /// <summary>
    ///     Visits an AIP function call.
    /// </summary>
    public Expression Visit(Function node) {
        if (_dynamic) {
            throw new ParseException("AIP functions are not supported in dynamic evaluation.", node.Position);
        }

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
            return GuardAccess(source, Expression.Property(source, "Item", Expression.Constant(key)));
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
            return GuardAccess(source, Expression.Call(method, source, Expression.Constant(index)));
        }

        if (TryAccess(source, key, out var access)) {
            return GuardAccess(source, access);
        }

        throw new ParseException($"Unknown field '{key}'.", field.Position);
    }

    private static bool TryAccess(Expression source, string name, out Expression expression) {
        var access = Common.MemberAccess.Resolve(source, name);
        expression = access!;
        return access is not null;
    }

    private bool TryBuildRepeatedHas(IComparableArg comparable, IArg arg, out Expression expression) {
        expression = null!;
        if (comparable is not Member { Fields.Count: > 0 } member || member.Fields[0] is not Text) {
            return false;
        }

        var source = VisitValue(member.Value, true);
        if (source.Type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(source.Type)) {
            return false;
        }

        var elementType = source.Type.GetElementType()
                       ?? source.Type.GenericTypeArguments.FirstOrDefault() ?? typeof(object);
        var item = Expression.Parameter(elementType, "item");

        // Isolate inner-lambda guards: GuardAccess accumulations on `item` reference a
        // parameter that lives only inside the inner Any(...) lambda, so they wrap
        // the lambda body and stay out of the outer comparison guard.
        var outerGuard = _guard;
        _guard = null;

        Expression left = item;
        foreach (var field in member.Fields) {
            left = Access(left, field);
        }

        var right     = Visit(arg);
        var body      = BuildEqual(left, right);
        var innerGuard = _guard;
        if (innerGuard is not null) {
            body = Expression.AndAlso(innerGuard, body);
        }

        _guard = outerGuard;

        var method = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                       .Single(m => m.Name == nameof(Enumerable.Any)
                                                 && m.GetParameters().Length == 2)
                                       .MakeGenericMethod(elementType);
        expression = GuardAccess(source, Expression.Call(method, source, Expression.Lambda(body, item)));
        return true;
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
            // wildcards, producing a stable comparison result.
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
            if (left.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(left.Type)) {
                return CollectionPresence(left);
            }

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
        if (expression is ConstantExpression { Value: string text }) {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(DateTimeOffset)
             && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var offset)) {
                return Expression.Constant(offset, type);
            }

            if (target == typeof(DateTime)
             && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out offset)) {
                return Expression.Constant(offset.UtcDateTime, type);
            }

            if (target == typeof(TimeSpan) && TryParseSeconds(text, out var duration)) {
                return Expression.Constant(duration, type);
            }
        }

        return expression.Type == type ? expression : Expression.Convert(expression, type);
    }

    private static bool TryParseSeconds(string text, out TimeSpan duration) {
        duration = TimeSpan.Zero;
        if (!text.EndsWith("s", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) {
            return false;
        }

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private Expression GuardAccess(Expression source, Expression access) {
        if (source.Type.IsValueType && Nullable.GetUnderlyingType(source.Type) is null) {
            return access;
        }

        var notNull = Expression.NotEqual(source, Expression.Constant(null, source.Type));
        _guard = _guard is null ? notNull : Expression.AndAlso(_guard, notNull);

        return Expression.Condition(notNull, access, Expression.Default(access.Type));
    }

    private static Expression ToBoolean(Expression expression) {
        if (expression.Type == typeof(bool)) {
            return expression;
        }

        if (expression.Type.IsValueType && Nullable.GetUnderlyingType(expression.Type) is null) {
            return Expression.Constant(true);
        }

        if (expression.Type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(expression.Type)) {
            return CollectionPresence(expression);
        }

        return Expression.NotEqual(expression, Expression.Constant(null, expression.Type));
    }

    // AIP-160 presence on a repeated field is satisfied only when it holds at least one element,
    // not merely when the collection reference is non-null.
    private static Expression CollectionPresence(Expression collection) {
        var elementType = collection.Type.GetElementType()
                       ?? collection.Type.GenericTypeArguments.FirstOrDefault() ?? typeof(object);
        var any = typeof(Enumerable).GetMethods(BindingFlags.Static | BindingFlags.Public)
                                    .Single(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 1)
                                    .MakeGenericMethod(elementType);
        return Expression.AndAlso(
            Expression.NotEqual(collection, Expression.Constant(null, collection.Type)),
            Expression.Call(any, collection));
    }

    private static Expression Combine(
        IEnumerable<Expression>                        expressions,
        Func<Expression, Expression, BinaryExpression> merge
    ) {
        return expressions.Aggregate(merge);
    }

    private static MethodInfo Method(string name) {
        return typeof(DynamicValues).GetMethod(name)!;
    }

    private Expression VisitDynamic(Member node) {
        if (node.Fields.Count == 0) {
            return DynamicConstant(node.Value);
        }

        if (node.Value is not Text head) {
            throw new ParseException("Expected a field name.", node.Position);
        }

        var expression = MemberAccess(Parameter, head.Value);
        foreach (var field in node.Fields) {
            var name = field switch {
                Text text       => text.Value,
                Integer integer => integer.Value.ToString(CultureInfo.InvariantCulture),
                var _           => throw new ParseException("Unsupported AIP field.", field.Position),
            };
            expression = MemberAccess(expression, name);
        }

        return expression;
    }

    private Expression VisitDynamic(Restriction node) {
        var left = Visit(node.Comparable);

        if (node.Comparator is null || node.Arg is null) {
            return Expression.Call(TruthyMethod, ToObject(left));
        }

        if (node.Comparator is Has) {
            throw new ParseException("AIP membership is not supported in dynamic evaluation.", node.Position);
        }

        var right = Visit(node.Arg);
        var method = node.Comparator switch {
            Equal              => EqualMethod,
            NotEqual           => NotEqualMethod,
            LessThan           => LessMethod,
            LessThanOrEqual    => LessOrEqualMethod,
            GreaterThan        => GreaterMethod,
            GreaterThanOrEqual => GreaterOrEqualMethod,
            var _              => throw new ParseException("Unsupported AIP comparator in dynamic evaluation.",
                                                          node.Position),
        };

        return Expression.Call(method, ToObject(left), ToObject(right));
    }

    private Expression MemberAccess(Expression container, string name) {
        return Expression.Call(MemberMethod, ToObject(container), Expression.Constant(name, typeof(string)));
    }

    private static Expression DynamicConstant(IValue value) {
        object? constant = value switch {
            Text text       => text.Value,
            Integer integer => integer.Value,
            Number number   => number.Value,
            Truth truth     => truth.Value,
            Null            => null,
            var _           => throw new ParseException("Unsupported AIP value.", value.Position),
        };

        return Expression.Constant(constant, typeof(object));
    }

    private static Expression ToObject(Expression expression) {
        return expression.Type == typeof(object) ? expression : Expression.Convert(expression, typeof(object));
    }
}
