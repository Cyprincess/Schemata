using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Schemata.Common;

namespace Schemata.Entity.Cache;

/// <summary>
///     Serializes a LINQ expression tree into a deterministic string for use as a cache key.
/// </summary>
/// <remarks>
///     <para>
///         Lambda parameters are rewritten as <c>_p0</c>, <c>_p1</c>, … in discovery order so that equivalent
///         expressions with differently-named parameters produce the same output.
///     </para>
///     <para>
///         Constants are whitelisted by runtime type and rendered with
///         <see cref="CultureInfo.InvariantCulture" />, so keys are stable across locales;
///         constants outside the whitelist make <see cref="ToString(Expression)" /> return
///         <see langword="null" /> instead of invoking arbitrary user code.
///     </para>
///     <para>
///         Method calls include their declaring type, generic arguments, and parameter types
///         to distinguish overloads and extension methods.
///     </para>
/// </remarks>
public class Stringizing : ExpressionVisitor
{
    private readonly Dictionary<ParameterExpression, string> _aliases = new();
    private readonly StringBuilder                           _builder = new();
    private bool _failed;

    // Structure mode accompanies a provider-owned key: the provider's own root node renders
    // as an opaque marker because the key already carries the source and command, while any
    // further extension node stays uncacheable. Standalone mode has no such companion, so a
    // queryable constant has no safe identity and fails the key.
    private bool _structure;
    private int  _extensions;

    /// <summary>Converts <paramref name="expression" /> to its deterministic string representation.</summary>
    /// <remarks>
    ///     Captured local variables and other closed sub-expressions are folded to constants
    ///     via <see cref="Evaluator.PartialEval(Expression, Func{Expression, bool})" /> before
    ///     serialization, so different values of a captured variable produce different keys.
    /// </remarks>
    /// <param name="expression">The expression to serialize.</param>
    /// <returns>
    ///     The deterministic string representation, or <see langword="null" /> when the tree
    ///     contains a constant outside the supported whitelist.
    /// </returns>
    public static string? ToString(Expression expression) {
        var stringizing = new Stringizing();

        stringizing.Visit(Evaluator.PartialEval(expression));

        return stringizing._failed ? null : stringizing.ToString();
    }

    /// <summary>
    ///     Renders the query structure that accompanies a provider cache-key fragment: the
    ///     provider's own root node is an opaque marker, and any further extension node leaves
    ///     the result <see langword="null" />. Client-side projection identity comes from the
    ///     member and method identities, lambda structure, and safely encoded closure constants.
    /// </summary>
    public static string? ToStructure(Expression expression) {
        var stringizing = new Stringizing { _structure = true };

        stringizing.Visit(Evaluator.PartialEval(expression));

        return stringizing._failed ? null : stringizing.ToString();
    }

    protected override Expression VisitLambda<T>(Expression<T> node) {
        _builder.Append('(');

        var parameters = node.Parameters;
        for (var i = 0; i < parameters.Count; i++) {
            if (i > 0) {
                _builder.Append(", ");
            }

            Visit(parameters[i]);
        }

        _builder.Append(") => ");

        Visit(node.Body);

        return node;
    }

    protected override Expression VisitParameter(ParameterExpression node) {
        if (!_aliases.TryGetValue(node, out var alias)) {
            alias          = $"_p{_aliases.Count}";
            _aliases[node] = alias;
        }

        _builder.Append(alias).Append(':').Append(node.Type.FullName ?? node.Type.Name);

        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node) {
        AppendConstant(node.Value);

        return node;
    }

    protected override Expression VisitExtension(Expression node) {
        if (!_structure) {
            // Extension semantics belong to the owning provider; standalone rendering cannot
            // represent them, and the inherited traversal would silently visit children.
            _failed = true;

            return node;
        }

        if (++_extensions > 1) {
            _failed = true;
            return node;
        }

        _builder.Append("xroot:").Append(node.GetType().Name);

        return node;
    }

    // The inherited traversal would silently visit children of node kinds without a dedicated
    // renderer, dropping the semantics that distinguish them; refuse those kinds instead.
    public override Expression? Visit(Expression? node) {
        switch (node?.NodeType) {
            case ExpressionType.NewArrayInit:
            case ExpressionType.NewArrayBounds:
            case ExpressionType.ListInit:
            case ExpressionType.Index:
            case ExpressionType.Invoke:
            case ExpressionType.Block:
            case ExpressionType.Default:
            case ExpressionType.Goto:
            case ExpressionType.Label:
            case ExpressionType.Loop:
            case ExpressionType.Switch:
            case ExpressionType.Try:
            case ExpressionType.RuntimeVariables:
                _failed = true;
                return node;
            default:
                return base.Visit(node);
        }
    }

    // Constants are whitelisted by runtime type: rendering unknown references with ToString
    // would execute user code at key-computation time and collide distinct values.
    private void AppendConstant(object? value) {
        switch (value) {
            case null:
                _builder.Append("null");
                return;
            case string str:
                AppendQuoted(str);
                return;
            case char c:
                _builder.Append('\'');
                AppendQuoted(c.ToString());
                _builder.Append('\'');
                return;
            case bool flag:
                _builder.Append(flag ? "true" : "false");
                return;
            case sbyte v:
                _builder.Append("i1:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case byte v:
                _builder.Append("u1:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case short v:
                _builder.Append("i2:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case ushort v:
                _builder.Append("u2:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case int v:
                _builder.Append("i4:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case uint v:
                _builder.Append("u4:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case long v:
                _builder.Append("i8:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case ulong v:
                _builder.Append("u8:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case float v:
                _builder.Append("f4:").Append(v.ToString("R", CultureInfo.InvariantCulture));
                return;
            case double v:
                _builder.Append("f8:").Append(v.ToString("R", CultureInfo.InvariantCulture));
                return;
            case decimal v:
                _builder.Append("d:").Append(v.ToString(CultureInfo.InvariantCulture));
                return;
            case Guid v:
                _builder.Append(v.ToString("D"));
                return;
            case DateTime v:
                _builder.Append("dt").Append(v.Ticks.ToString(CultureInfo.InvariantCulture)).Append(':').Append(((int)v.Kind).ToString(CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset v:
                _builder.Append("dto").Append(v.UtcTicks.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v.Offset.Ticks.ToString(CultureInfo.InvariantCulture));
                return;
            case TimeSpan v:
                _builder.Append("ts").Append(v.Ticks.ToString(CultureInfo.InvariantCulture));
                return;
            case Enum v:
                _builder.Append('e').Append(v.GetType().FullName ?? v.GetType().Name).Append(':').Append(Convert.ToDecimal(v).ToString(CultureInfo.InvariantCulture));
                return;
            case Type type:
                _builder.Append(type.FullName ?? type.Name);
                return;
            case byte[] bytes:
                _builder.Append('b').Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Convert.ToHexString(bytes));
                return;
            case Array array:
                AppendArray(array);
                return;
            case IQueryable queryable when _structure:
                _builder.Append("root:").Append(queryable.GetType().FullName ?? queryable.GetType().Name);
                return;
            case IQueryable:
                _failed = true;
                return;
            default:
                _failed = true;
                return;
        }
    }

    private void AppendQuoted(string value) {
        _builder.Append('"');

        foreach (var c in value) {
            switch (c) {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                default:
                    if (char.IsControl(c)) {
                        _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    } else {
                        _builder.Append(c);
                    }
                    break;
            }
        }

        _builder.Append('"');
    }

    private void AppendArray(Array array) {
        var element = array.GetType().GetElementType();
        if (array.Rank != 1 || array.GetLowerBound(0) != 0 || element is null) {
            _failed = true;
            return;
        }

        _builder.Append('[').Append(element.FullName ?? element.Name).Append(':').Append(array.Length.ToString(CultureInfo.InvariantCulture)).Append(":[");

        for (var i = 0; i < array.Length; i++) {
            if (i > 0) {
                _builder.Append(',');
            }

            AppendConstant(array.GetValue(i));
        }

        _builder.Append("]]");
    }

    protected override Expression VisitBinary(BinaryExpression node) {
        _builder.Append('(');

        Visit(node.Left);

        _builder.Append(' ')
                .Append(node.NodeType switch {
                     ExpressionType.Add                => "+",
                     ExpressionType.Subtract           => "-",
                     ExpressionType.Multiply           => "*",
                     ExpressionType.Divide             => "/",
                     ExpressionType.Modulo             => "%",
                     ExpressionType.Equal              => "==",
                     ExpressionType.NotEqual           => "!=",
                     ExpressionType.GreaterThan        => ">",
                     ExpressionType.GreaterThanOrEqual => ">=",
                     ExpressionType.LessThan           => "<",
                     ExpressionType.LessThanOrEqual    => "<=",
                     ExpressionType.AndAlso            => "&&",
                     ExpressionType.OrElse             => "||",
                     ExpressionType.And                => "&",
                     ExpressionType.Or                 => "|",
                     ExpressionType.ExclusiveOr        => "^",
                     var _                             => node.NodeType.ToString(),
                 })
                .Append(' ');

        Visit(node.Right);

        _builder.Append(')');

        return node;
    }

    protected override Expression VisitMember(MemberExpression node) {
        Visit(node.Expression);

        _builder.Append('.').Append(node.Member.DeclaringType?.FullName ?? node.Member.DeclaringType?.Name)
                .Append(':').Append(node.Member.Name);

        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node) {
        var method      = node.Method;
        var isExtension = method.IsStatic && method.IsDefined(typeof(ExtensionAttribute), false);

        if (isExtension) {
            Visit(node.Arguments[0]);
        } else if (node.Object is null) {
            _builder.Append(method.DeclaringType?.FullName ?? method.DeclaringType?.Name);
        } else {
            Visit(node.Object);
        }

        AppendMethodHead(method);
        AppendArguments(node.Arguments, isExtension ? 1 : 0);

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node) {
        switch (node.NodeType) {
            case ExpressionType.Not:
                _builder.Append('!');
                Visit(node.Operand);
                return node;
            case ExpressionType.Negate:
                _builder.Append('-');
                Visit(node.Operand);
                return node;
            case ExpressionType.UnaryPlus:
                _builder.Append('+');
                Visit(node.Operand);
                return node;
            case ExpressionType.Quote:
                Visit(node.Operand);
                return node;
            case ExpressionType.Convert:
            case ExpressionType.ConvertChecked:
            case ExpressionType.TypeAs:
                _builder.Append('(').Append(node.Type.FullName ?? node.Type.Name).Append(')');
                Visit(node.Operand);
                return node;
            default:
                _builder.Append(node.NodeType.ToString()).Append(':');
                Visit(node.Operand);
                return node;
        }
    }

    protected override Expression VisitConditional(ConditionalExpression node) {
        _builder.Append('(');
        Visit(node.Test);
        _builder.Append(" ? ");
        Visit(node.IfTrue);
        _builder.Append(" : ");
        Visit(node.IfFalse);
        _builder.Append(')');
        return node;
    }

    protected override Expression VisitNew(NewExpression node) {
        _builder.Append("new ").Append(node.Type.FullName ?? node.Type.Name).Append('(');

        for (var i = 0; i < node.Arguments.Count; i++) {
            if (i > 0) {
                _builder.Append(", ");
            }

            Visit(node.Arguments[i]);
        }

        _builder.Append(')');
        return node;
    }

    protected override Expression VisitMemberInit(MemberInitExpression node) {
        Visit(node.NewExpression);

        _builder.Append(" { ");

        for (var i = 0; i < node.Bindings.Count; i++) {
            if (i > 0) {
                _builder.Append(", ");
            }

            VisitMemberBinding(node.Bindings[i]);
        }

        _builder.Append(" }");
        return node;
    }

    protected override MemberBinding VisitMemberBinding(MemberBinding node) {
        switch (node) {
            case MemberAssignment assignment:
                _builder.Append(assignment.Member.Name).Append(" = ");
                Visit(assignment.Expression);
                return node;
            case MemberListBinding listBinding:
                _builder.Append(listBinding.Member.Name).Append(" = [");

                for (var i = 0; i < listBinding.Initializers.Count; i++) {
                    if (i > 0) {
                        _builder.Append(", ");
                    }

                    var init = listBinding.Initializers[i];
                    _builder.Append(init.AddMethod.Name).Append('(');

                    for (var j = 0; j < init.Arguments.Count; j++) {
                        if (j > 0) {
                            _builder.Append(", ");
                        }

                        Visit(init.Arguments[j]);
                    }

                    _builder.Append(')');
                }

                _builder.Append(']');
                return node;
            case MemberMemberBinding memberBinding:
                _builder.Append(memberBinding.Member.Name).Append(" = { ");

                for (var i = 0; i < memberBinding.Bindings.Count; i++) {
                    if (i > 0) {
                        _builder.Append(", ");
                    }

                    VisitMemberBinding(memberBinding.Bindings[i]);
                }

                _builder.Append(" }");
                return node;
            default:
                return base.VisitMemberBinding(node);
        }
    }

    protected override Expression VisitTypeBinary(TypeBinaryExpression node) {
        _builder.Append('(');
        Visit(node.Expression);
        _builder.Append(node.NodeType == ExpressionType.TypeEqual ? " TypeEqual " : " is ");
        _builder.Append(node.TypeOperand.FullName ?? node.TypeOperand.Name);
        _builder.Append(')');
        return node;
    }

    public override string ToString() { return _builder.ToString(); }

    private void AppendMethodHead(MethodInfo method) {
        _builder.Append('.').Append(method.DeclaringType?.FullName ?? method.DeclaringType?.Name)
                .Append(':').Append(method.Name);

        if (method.IsGenericMethod) {
            AppendTypeList(method.GetGenericArguments());
        }

        _builder.Append('(');

        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++) {
            if (i > 0) {
                _builder.Append(',');
            }

            _builder.Append(parameters[i].ParameterType.FullName ?? parameters[i].ParameterType.Name);
        }

        _builder.Append(")(");
    }

    private void AppendTypeList(Type[] types) {
        _builder.Append("<<");

        for (var i = 0; i < types.Length; i++) {
            if (i > 0) {
                _builder.Append(',');
            }

            _builder.Append(types[i].FullName ?? types[i].Name);
        }

        _builder.Append('>');
    }

    private void AppendArguments(ReadOnlyCollection<Expression> arguments, int start) {
        var first = true;
        for (var i = start; i < arguments.Count; i++) {
            if (!first) {
                _builder.Append(", ");
            }

            first = false;
            Visit(arguments[i]);
        }

        _builder.Append(')');
    }
}
