using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Resource.Foundation.Grammars.Expressions;

namespace Schemata.Resource.Foundation.Grammars;

/// <summary>
///     Holds parameter bindings, expression bindings, and custom function registrations
///     used during LINQ expression building from parsed filter tokens
///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>.
/// </summary>
public class Container
{
    private static readonly ConcurrentDictionary<string, MethodInfo> MethodCache = [];

    /// <summary>
    ///     Initializes a new container for the specified token.
    /// </summary>
    /// <param name="token">The root <see cref="IToken" />.</param>
    public Container(IToken token) { Token = token; }

    /// <summary>
    ///     Gets the registered custom filter functions keyed by name.
    /// </summary>
    public Dictionary<string, FilterFunction> Functions { get; } = [];

    private IToken Token { get; }

    private Dictionary<string, Expression> Expressions { get; } = [];

    private Dictionary<string, ParameterExpression> Parameters { get; } = [];

    /// <summary>
    ///     Creates a container for the specified token.
    /// </summary>
    /// <param name="token">The root <see cref="IToken" />.</param>
    /// <returns>A new <see cref="Container" />.</returns>
    public static Container Build(IToken token) { return new(token); }

    /// <summary>
    ///     Registers a custom function callable in filter expressions.
    /// </summary>
    /// <param name="name">The function name as it appears in the filter string.</param>
    /// <param name="factory">A factory producing an expression from arguments and the container.</param>
    /// <returns>This container for chaining.</returns>
    public Container RegisterFunction(string name, Func<Expression[], Container, Expression> factory) {
        Functions[name] = new(factory);
        return this;
    }

    /// <summary>
    ///     Binds a named parameter expression of the specified type.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="type">The parameter type.</param>
    /// <returns>This container for chaining.</returns>
    public Container Bind(string name, Type type) {
        var parameter = Expression.Parameter(type, name);

        Parameters.Add(name, parameter);

        return this;
    }

    /// <summary>
    ///     Binds a named expression (e.g. property access) for use in filter resolution.
    /// </summary>
    /// <param name="name">The expression name.</param>
    /// <param name="expression">The LINQ expression.</param>
    /// <returns>This container for chaining.</returns>
    public Container Bind(string name, Expression expression) {
        Expressions.Add(name, expression);

        return this;
    }

    /// <summary>
    ///     Attempts to retrieve a bound expression by name.
    /// </summary>
    /// <param name="name">The expression name.</param>
    /// <param name="expression">The bound expression, if found.</param>
    /// <returns><see langword="true" /> if found.</returns>
    public bool TryGetExpression(string? name, out Expression? expression) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return Expressions.TryGetValue(name, out expression);
        }

        expression = null;
        return false;
    }

    /// <summary>
    ///     Attempts to retrieve a bound parameter expression by name.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="parameter">The parameter expression, if found.</param>
    /// <returns><see langword="true" /> if found.</returns>
    public bool TryGetParameter(string? name, out ParameterExpression? parameter) {
        if (!string.IsNullOrWhiteSpace(name)) {
            return Parameters.TryGetValue(name, out parameter);
        }

        parameter = null;
        return false;
    }

    /// <summary>
    ///     Gets a cached <see cref="MethodInfo" /> by type, name, and parameter types.
    /// </summary>
    public MethodInfo? GetMethod(
        Type          type,
        string        name,
        Type[]?       types,
        BindingFlags? flag = null
    ) {
        return GetMethod(type, name, types, () => {
            flag ??= BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;

            return types is null
                ? type.GetMethod(name, flag.Value)
                : type.GetMethod(name, flag.Value, null, types, null);
        });
    }

    /// <summary>
    ///     Gets a cached <see cref="MethodInfo" /> with a custom getter factory
    ///     for complex method resolution.
    /// </summary>
    public MethodInfo? GetMethod(
        Type               type,
        string             name,
        IEnumerable<Type>? types,
        Func<MethodInfo?>  getter
    ) {
        var typ       = types?.Aggregate("", (s, i) => $"{s}{i.Name},");
        var qualified = $"{type.FullName}.{name}({typ})";

        if (MethodCache.TryGetValue(qualified, out var method)) {
            return method;
        }

        method = getter();

        if (method is null) {
            return null;
        }

        MethodCache[qualified] = method;

        return method;
    }

    /// <summary>
    ///     Builds a lambda expression from the root token using all registered bindings.
    /// </summary>
    /// <returns>The lambda expression, or <see langword="null" /> if the token produces no expression.</returns>
    public LambdaExpression? Build() {
        var body = Token.ToExpression(this);
        if (body is null) {
            return null;
        }

        return Expression.Lambda(body, Parameters.Values);
    }
}
