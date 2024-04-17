using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Resource.Foundation.Grammars.Terms;
using Expression = System.Linq.Expressions.Expression;

namespace Schemata.Resource.Foundation.Grammars;

public class Container
{
    private static readonly Dictionary<string, MethodInfo> MethodCache = [];

    public Container(IToken token) {
        Token = token;
    }

    public bool AllowFunctions { get; private set; }

    public Dictionary<Type, Dictionary<string, bool>> Functions { get; } = [];

    private IToken Token { get; }

    private Dictionary<string, Expression> Expressions { get; } = [];

    private Dictionary<string, ParameterExpression> Parameters { get; } = [];

    public static Container Build(IToken token) {
        return new(token);
    }

    public Container AllowFunction<T>(string method) {
        return AllowFunction(typeof(T), method);
    }

    public Container AllowFunction(Type type, string method) {
        AllowFunctions = true;

        if (!Functions.TryGetValue(type, out var methods)) {
            methods = new();

            Functions[type] = methods;
        }

        methods[method] = true;

        return this;
    }

    public Container Bind(string name, Type type) {
        var parameter = Expression.Parameter(type, name);

        Parameters.Add(name, parameter);

        return this;
    }

    public Container Bind(string name, Expression expression) {
        Expressions.Add(name, expression);

        return this;
    }

    public bool TryGetExpression(string? name, out Expression? expression) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return Expressions.TryGetValue(name, out expression);
        }

        expression = default;
        return false;
    }

    public bool TryGetParameter(string? name, out ParameterExpression? parameter) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return Parameters.TryGetValue(name, out parameter);
        }

        parameter = null;
        return false;
    }

    public MethodInfo? GetMethod(
        Type          type,
        string        name,
        Type[]?       types,
        BindingFlags? flag = null) {
        return GetMethod(type,
            name,
            types,
            () => {
                flag ??= BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

                return types == null
                    ? type.GetMethod(name, flag.Value)
                    : type.GetMethod(name, flag.Value, null, types, null);
            });
    }

    public MethodInfo? GetMethod(
        Type               type,
        string             name,
        IEnumerable<Type>? types,
        Func<MethodInfo?>  getter) {
        var typ       = types?.Aggregate("", (s, i) => $"{s}{i.Name},");
        var qualified = $"{type.FullName}.{name}({typ})";

        if (MethodCache.TryGetValue(qualified, out var method)) {
            return method;
        }

        method = getter();

        if (method == null) return null;

        MethodCache.Add(qualified, method);

        return method;
    }

    public LambdaExpression? Build() {
        var body = Token.ToExpression(this);
        if (body is null) {
            return null;
        }

        return Expression.Lambda(body, Parameters.Values);
    }
}
