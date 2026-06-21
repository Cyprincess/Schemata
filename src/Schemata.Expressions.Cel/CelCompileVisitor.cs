using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Humanizer;
using Parlot;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     Converts CEL AST nodes into LINQ expression tree nodes.
/// </summary>
internal sealed class CelCompileVisitor
{
    private static readonly MethodInfo MemberMethod         = Method(nameof(DynamicValues.Member));
    private static readonly MethodInfo TruthyMethod         = Method(nameof(DynamicValues.Truthy));
    private static readonly MethodInfo IsPresentMethod      = Method(nameof(DynamicValues.IsPresent));
    private static readonly MethodInfo EqualMethod          = Method(nameof(DynamicValues.Equal));
    private static readonly MethodInfo NotEqualMethod       = Method(nameof(DynamicValues.NotEqual));
    private static readonly MethodInfo LessMethod           = Method(nameof(DynamicValues.Less));
    private static readonly MethodInfo LessOrEqualMethod    = Method(nameof(DynamicValues.LessOrEqual));
    private static readonly MethodInfo GreaterMethod        = Method(nameof(DynamicValues.Greater));
    private static readonly MethodInfo GreaterOrEqualMethod = Method(nameof(DynamicValues.GreaterOrEqual));
    private static readonly MethodInfo AddMethod            = Method(nameof(DynamicValues.Add));
    private static readonly MethodInfo SubtractMethod       = Method(nameof(DynamicValues.Subtract));
    private static readonly MethodInfo MultiplyMethod       = Method(nameof(DynamicValues.Multiply));
    private static readonly MethodInfo DivideMethod         = Method(nameof(DynamicValues.Divide));
    private static readonly MethodInfo ModuloMethod         = Method(nameof(DynamicValues.Modulo));

    private readonly Stack<Dictionary<string, ParameterExpression>> _scopes = new();
    private readonly bool                                           _dynamic;

    /// <summary>
    ///     Creates a visitor for expressions evaluated against the supplied context type.
    /// </summary>
    public CelCompileVisitor(Type contextType, ExpressionCompileOptions? options) {
        _dynamic  = typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(contextType);
        Parameter = Expression.Parameter(contextType, LowerFirst(contextType.Name));
    }

    /// <summary>
    ///     Gets the root parameter used by generated expressions.
    /// </summary>
    public ParameterExpression Parameter { get; }

    /// <summary>
    ///     Visits a CEL AST node.
    /// </summary>
    public Expression Visit(CelNode node) {
        return node switch {
            CelConstant constant       => _dynamic ? DynamicConstant(constant) : Expression.Constant(constant.Value),
            CelIdentifier identifier   => Visit(identifier),
            CelMember member           => _dynamic ? MemberAccess(Visit(member.Target), member.Member) : Access(Visit(member.Target), member.Member),
            CelUnary unary             => BuildUnary(unary),
            CelBinary binary           => BuildBinary(binary),
            CelCall call               => BuildCall(call),
            CelMemberCall call         => _dynamic ? UnsupportedDynamic("CEL member calls") : BuildMemberCall(call),
            CelConditional conditional => _dynamic ? UnsupportedDynamic("CEL conditional expressions") : BuildConditional(conditional),
            CelList list               => _dynamic ? UnsupportedDynamic("CEL list literals") : BuildList(list),
            CelMap map                 => _dynamic ? UnsupportedDynamic("CEL map literals") : BuildMap(map),
            var _                      => throw new ParseException("Unsupported CEL node.", default),
        };
    }

    private Expression Visit(CelIdentifier node) {
        if (_dynamic) {
            return MemberAccess(Parameter, node.Name);
        }

        foreach (var scope in _scopes) {
            if (scope.TryGetValue(node.Name, out var local)) {
                return local;
            }
        }

        if (string.Equals(node.Name, Parameter.Name, StringComparison.Ordinal)) {
            return Parameter;
        }

        if (TryAccess(Parameter, node.Name, out var access)) {
            return access;
        }

        throw new ParseException($"undeclared reference to '{node.Name}' (in container '')", default);
    }

    private Expression BuildUnary(CelUnary node) {
        if (_dynamic) {
            return BuildDynamicUnary(node);
        }

        var operand = Visit(node.Operand);
        return node.Operator switch {
            "!"   => Expression.Not(ToBoolean(operand)),
            "-"   => Expression.Negate(operand),
            var _ => throw new ParseException($"Unsupported CEL unary operator '{node.Operator}'.", default),
        };
    }

    private Expression BuildBinary(CelBinary node) {
        if (_dynamic) {
            return BuildDynamicBinary(node);
        }

        var left  = Visit(node.Left);
        var right = Visit(node.Right);

        return node.Operator switch {
            "&&"  => Expression.AndAlso(ToBoolean(left), ToBoolean(right)),
            "||"  => Expression.OrElse(ToBoolean(left), ToBoolean(right)),
            "+"   => Expression.Add(left, ConvertIfNeeded(right, left.Type)),
            "-"   => Expression.Subtract(left, ConvertIfNeeded(right, left.Type)),
            "*"   => Expression.Multiply(left, ConvertIfNeeded(right, left.Type)),
            "/"   => Expression.Divide(left, ConvertIfNeeded(right, left.Type)),
            "%"   => Expression.Modulo(left, ConvertIfNeeded(right, left.Type)),
            "=="  => Expression.Equal(left, ConvertIfNeeded(right, left.Type)),
            "!="  => Expression.NotEqual(left, ConvertIfNeeded(right, left.Type)),
            "<"   => Expression.LessThan(left, ConvertIfNeeded(right, left.Type)),
            "<="  => Expression.LessThanOrEqual(left, ConvertIfNeeded(right, left.Type)),
            ">"   => Expression.GreaterThan(left, ConvertIfNeeded(right, left.Type)),
            ">="  => Expression.GreaterThanOrEqual(left, ConvertIfNeeded(right, left.Type)),
            "in"  => BuildContains(right, left),
            var _ => throw new ParseException($"Unsupported CEL binary operator '{node.Operator}'.", default),
        };
    }

    private Expression BuildCall(CelCall node) {
        if (_dynamic) {
            return BuildDynamicCall(node);
        }

        if (node.Name == "has" && node.Args.Count == 1) {
            return BuildHas(node.Args[0]);
        }

        if (node.Name == "size" && node.Args.Count == 1) {
            return BuildSize(Visit(node.Args[0]));
        }

        throw new ParseException($"Unsupported CEL function '{node.Name}'.", default);
    }

    private Expression BuildMemberCall(CelMemberCall node) {
        var target = Visit(node.Target);
        if (node.Name is "exists" or "all" or "filter" or "map") {
            return BuildMacro(node.Name, target, node.Args);
        }

        return node.Name switch {
            "contains" when node.Args.Count == 1 => BuildContains(target, Visit(node.Args[0])),
            "startsWith" when node.Args.Count == 1 =>
                CallString(target, nameof(string.StartsWith), Visit(node.Args[0])),
            "endsWith" when node.Args.Count == 1 => CallString(target, nameof(string.EndsWith), Visit(node.Args[0])),
            "matches" when node.Args.Count == 1 => Expression.Call(
                typeof(CelCompileVisitor).GetMethod(nameof(IsRegexMatch),
                                                    BindingFlags.Static | BindingFlags.NonPublic)!,
                ConvertIfNeeded(target, typeof(string)), ConvertIfNeeded(Visit(node.Args[0]), typeof(string))),
            "size" when node.Args.Count == 0 => BuildSize(target),
            var _ => throw new ParseException($"Unsupported CEL member function '{node.Name}'.", default),
        };
    }

    private Expression BuildConditional(CelConditional node) {
        var whenTrue  = Visit(node.WhenTrue);
        var whenFalse = Visit(node.WhenFalse);
        var type      = CommonType(whenTrue.Type, whenFalse.Type);
        return Expression.Condition(ToBoolean(Visit(node.Condition)), ConvertIfNeeded(whenTrue, type),
                                    ConvertIfNeeded(whenFalse, type));
    }

    private Expression BuildList(CelList node) {
        var add = typeof(List<object?>).GetMethod(nameof(List<>.Add), [typeof(object)])!;
        return Expression.ListInit(Expression.New(typeof(List<object?>)),
                                   node.Items.Select(item => Expression.ElementInit(
                                                         add, ConvertIfNeeded(Visit(item), typeof(object)))));
    }

    private Expression BuildMap(CelMap node) {
        var add = typeof(Dictionary<object, object?>).GetMethod(nameof(Dictionary<,>.Add), [typeof(object), typeof(object),
                                                                ])!;
        return Expression.ListInit(Expression.New(typeof(Dictionary<object, object?>)),
                                   node.Entries.Select(entry => Expression.ElementInit(
                                                           add, ConvertIfNeeded(Visit(entry.Key), typeof(object)),
                                                           ConvertIfNeeded(Visit(entry.Value), typeof(object)))));
    }

    private static Expression Access(Expression source, string name) {
        if (TryAccess(source, name, out var expression)) {
            return expression;
        }

        throw new ParseException($"Unknown member '{name}'.", default);
    }

    private Expression BuildMacro(string name, Expression source, IReadOnlyList<CelNode> args) {
        if (args.Count != 2 || args[0] is not CelIdentifier identifier) {
            throw new ParseException($"CEL macro '{name}' requires an iteration variable and expression.", default);
        }

        var elementType = GetElementType(source.Type);
        var parameter   = Expression.Parameter(elementType, identifier.Name);
        _scopes.Push(new() { [identifier.Name] = parameter });
        try {
            var body = Visit(args[1]);
            return name switch {
                "exists" => CallEnumerable(nameof(Enumerable.Any), elementType, source,
                                           Expression.Lambda(ToBoolean(body), parameter)),
                "all" => CallEnumerable(nameof(Enumerable.All), elementType, source,
                                        Expression.Lambda(ToBoolean(body), parameter)),
                "filter" => Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), [elementType],
                                            CallEnumerable(nameof(Enumerable.Where), elementType, source,
                                                           Expression.Lambda(ToBoolean(body), parameter))),
                "map" => BuildMapMacro(source, elementType, parameter, body),
                var _ => throw new ParseException($"Unsupported CEL macro '{name}'.", default),
            };
        } finally {
            _scopes.Pop();
        }
    }

    private static Expression BuildMapMacro(
        Expression          source,
        Type                elementType,
        ParameterExpression parameter,
        Expression          body
    ) {
        var selected = Expression.Call(typeof(Enumerable), nameof(Enumerable.Select), [elementType, body.Type],
                                       source, Expression.Lambda(body, parameter));
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), [body.Type], selected);
    }

    private static Expression CallEnumerable(
        string           name,
        Type             elementType,
        Expression       source,
        LambdaExpression lambda
    ) {
        return Expression.Call(typeof(Enumerable), name, [elementType], source, lambda);
    }

    private static Expression BuildContains(Expression source, Expression value) {
        if (source.Type == typeof(string)) {
            return CallString(source, nameof(string.Contains), value);
        }

        if (TryGetDictionaryKeyType(source.Type, out var keyType)) {
            var containsKey = source.Type.GetMethod("ContainsKey", [keyType]);
            if (containsKey is not null) {
                return Expression.Call(source, containsKey, ConvertIfNeeded(value, keyType));
            }
        }

        var elementType = GetElementType(source.Type);
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [elementType], source,
                               ConvertIfNeeded(value, elementType));
    }

    private static Expression BuildSize(Expression source) {
        if (source.Type == typeof(string)) {
            return Expression.Property(source, nameof(string.Length));
        }

        if (source.Type.IsArray) {
            return Expression.ArrayLength(source);
        }

        if (TryAccess(source, "Count", out var count)) {
            return count;
        }

        var elementType = GetElementType(source.Type);
        return Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), [elementType], source);
    }

    private static Expression CallString(Expression source, string method, Expression argument) {
        return Expression.Call(ConvertIfNeeded(source, typeof(string)),
                               typeof(string).GetMethod(method, [typeof(string)])!,
                               ConvertIfNeeded(argument, typeof(string)));
    }

    private static Type GetElementType(Type type) {
        if (type.IsArray) {
            return type.GetElementType()!;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
            return type.GetGenericArguments()[0];
        }

        foreach (var item in type.GetInterfaces()) {
            if (item.IsGenericType && item.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                return item.GetGenericArguments()[0];
            }
        }

        throw new ParseException($"Type '{type}' is not enumerable.", default);
    }

    private static bool TryGetDictionaryKeyType(Type type, out Type keyType) {
        if (type.IsGenericType) {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>)
             || definition == typeof(IDictionary<,>)
             || definition == typeof(IReadOnlyDictionary<,>)) {
                keyType = type.GetGenericArguments()[0];
                return true;
            }
        }

        foreach (var item in type.GetInterfaces()) {
            if (!item.IsGenericType) {
                continue;
            }

            var definition = item.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) {
                keyType = item.GetGenericArguments()[0];
                return true;
            }
        }

        keyType = null!;
        return false;
    }

    private static bool TryGetDictionaryContainsKey(Type type, out MethodInfo method, out Type keyType) {
        keyType = null!;
        method  = null!;
        if (!TryGetDictionaryKeyType(type, out keyType)) {
            return false;
        }

        var direct = type.GetMethod("ContainsKey", [keyType]);
        if (direct is not null) {
            method = direct;
            return true;
        }

        foreach (var item in type.GetInterfaces()) {
            if (!item.IsGenericType) {
                continue;
            }

            var definition = item.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) {
                method = item.GetMethod("ContainsKey", [keyType])!;
                return true;
            }
        }

        return false;
    }

    private static Type CommonType(Type left, Type right) {
        if (left == right) {
            return left;
        }

        if (left.IsAssignableFrom(right)) {
            return left;
        }

        if (right.IsAssignableFrom(left)) {
            return right;
        }

        return typeof(object);
    }

    private static bool TryAccess(Expression source, string name, out Expression expression) {
        var member = source.Type.GetMember(name.Pascalize(), BindingFlags.Instance | BindingFlags.Public).FirstOrDefault();
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

    private Expression BuildHas(CelNode node) {
        if (node is CelMember member) {
            var receiver = Visit(member.Target);
            if (TryGetDictionaryContainsKey(receiver.Type, out var containsKey, out var keyType)
             && keyType == typeof(string)) {
                return Expression.Call(receiver, containsKey, Expression.Constant(member.Member, typeof(string)));
            }

            if (!TryAccess(receiver, member.Member, out var access)) {
                return Expression.Constant(false);
            }

            var present = Has(access);
            return receiver.Type.IsValueType && Nullable.GetUnderlyingType(receiver.Type) is null
                ? present
                : Expression.AndAlso(Expression.NotEqual(receiver, Expression.Constant(null, receiver.Type)), present);
        }

        if (node is CelIdentifier identifier) {
            foreach (var scope in _scopes) {
                if (scope.ContainsKey(identifier.Name)) {
                    return Expression.Constant(true);
                }
            }

            if (!string.Equals(identifier.Name, Parameter.Name, StringComparison.Ordinal)
             && !TryAccess(Parameter, identifier.Name, out _)) {
                throw new ParseException($"Undeclared identifier '{identifier.Name}' in has() macro.", default);
            }
        }

        return Has(Visit(node));
    }

    private static Expression Has(Expression expression) {
        if (expression.Type.IsValueType && Nullable.GetUnderlyingType(expression.Type) is null) {
            return Expression.Constant(true);
        }

        return Expression.NotEqual(expression, Expression.Constant(null, expression.Type));
    }

    private static Expression ConvertIfNeeded(Expression expression, Type type) {
        return expression.Type == type ? expression : Expression.Convert(expression, type);
    }

    private static Expression ToBoolean(Expression expression) {
        if (expression.Type == typeof(bool)) {
            return expression;
        }

        if (expression.Type == typeof(bool?)) {
            return Expression.Equal(expression, Expression.Constant(true, typeof(bool?)));
        }

        if (expression is ConstantExpression { Value: null }) {
            return Expression.Constant(false);
        }

        throw new ParseException($"Expression of type '{expression.Type}' cannot be used as a boolean.", default);
    }

    private static bool IsRegexMatch(string input, string pattern) {
        return Regex.IsMatch(input, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }

    private static MethodInfo Method(string name) {
        return typeof(DynamicValues).GetMethod(name)!;
    }

    private static Expression DynamicConstant(CelConstant value) {
        return Expression.Constant(value.Value, typeof(object));
    }

    private static Expression MemberAccess(Expression container, string name) {
        return Expression.Call(MemberMethod, ToObject(container), Expression.Constant(name, typeof(string)));
    }

    private Expression BuildDynamicUnary(CelUnary node) {
        var operand = Visit(node.Operand);
        return node.Operator switch {
            "!"   => Expression.Not(AsBoolean(operand)),
            "-"   => Expression.Call(MultiplyMethod, Expression.Constant((object)-1.0, typeof(object)), ToObject(operand)),
            var _ => throw new ParseException($"Unsupported CEL unary operator '{node.Operator}' in dynamic evaluation.",
                                             default),
        };
    }

    private Expression BuildDynamicBinary(CelBinary node) {
        var left  = Visit(node.Left);
        var right = Visit(node.Right);

        return node.Operator switch {
            "&&" => Expression.AndAlso(AsBoolean(left), AsBoolean(right)),
            "||" => Expression.OrElse(AsBoolean(left), AsBoolean(right)),
            "+"  => DynamicBinary(AddMethod, left, right),
            "-"  => DynamicBinary(SubtractMethod, left, right),
            "*"  => DynamicBinary(MultiplyMethod, left, right),
            "/"  => DynamicBinary(DivideMethod, left, right),
            "%"  => DynamicBinary(ModuloMethod, left, right),
            "==" => DynamicBinary(EqualMethod, left, right),
            "!=" => DynamicBinary(NotEqualMethod, left, right),
            "<"  => DynamicBinary(LessMethod, left, right),
            "<=" => DynamicBinary(LessOrEqualMethod, left, right),
            ">"  => DynamicBinary(GreaterMethod, left, right),
            ">=" => DynamicBinary(GreaterOrEqualMethod, left, right),
            "in" => throw new ParseException("CEL membership is not supported in dynamic evaluation.", default),
            var _ => throw new ParseException($"Unsupported CEL binary operator '{node.Operator}' in dynamic evaluation.",
                                             default),
        };
    }

    private Expression BuildDynamicCall(CelCall node) {
        if (node.Name == "has" && node.Args.Count == 1) {
            return Expression.Call(IsPresentMethod, ToObject(Visit(node.Args[0])));
        }

        throw new ParseException($"CEL function '{node.Name}' is not supported in dynamic evaluation.", default);
    }

    private static Expression DynamicBinary(MethodInfo method, Expression left, Expression right) {
        return Expression.Call(method, ToObject(left), ToObject(right));
    }

    private static Expression AsBoolean(Expression expression) {
        if (expression.Type == typeof(bool)) {
            return expression;
        }

        return Expression.Call(TruthyMethod, ToObject(expression));
    }

    private static Expression ToObject(Expression expression) {
        return expression.Type == typeof(object) ? expression : Expression.Convert(expression, typeof(object));
    }

    private static Expression UnsupportedDynamic(string construct) {
        throw new ParseException($"{construct} are not supported in dynamic evaluation.", default);
    }

    private static string LowerFirst(string name) {
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
