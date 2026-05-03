using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;

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
///         <see cref="IFormattable" /> values use <see cref="CultureInfo.InvariantCulture" />, so keys are stable
///         across locales.
///     </para>
///     <para>
///         Method calls include an <c>:arity</c> suffix to avoid overload collisions; static non-extension calls are
///         qualified with their declaring type.
///     </para>
/// </remarks>
public class Stringizing : ExpressionVisitor
{
    private readonly Dictionary<ParameterExpression, string> _aliases = new();
    private readonly StringBuilder                           _builder = new();

    /// <summary>Converts <paramref name="expression" /> to its deterministic string representation.</summary>
    /// <param name="expression">The expression to serialize.</param>
    /// <returns>The deterministic string, or <see langword="null" /> if serialization fails.</returns>
    public static string ToString(Expression expression) {
        var stringizing = new Stringizing();

        stringizing.Visit(expression);

        return stringizing.ToString();
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

        _builder.Append(alias);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node) {
        switch (node.Value) {
            case null:
                _builder.Append("null");
                return node;
            case string str:
                _builder.Append('"').Append(str).Append('"');
                return node;
            case IFormattable formattable:
                _builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                return node;
            default:
                _builder.Append(Convert.ToString(node.Value, CultureInfo.InvariantCulture) ?? string.Empty);
                return node;
        }
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

        _builder.Append('.').Append(node.Member.Name);

        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node) {
        var method = node.Method;
        var arity  = method.GetParameters().Length;
        // Extension methods are detected by [ExtensionAttribute] on a static method.
        var isExtension = method.IsStatic && method.IsDefined(typeof(ExtensionAttribute), false);

        if (isExtension) {
            Visit(node.Arguments[0]);
            AppendMethodHead(method.Name, arity);
            AppendArguments(node.Arguments, 1);
        } else if (node.Object is null) {
            _builder.Append(method.DeclaringType?.FullName ?? method.DeclaringType?.Name ?? string.Empty);
            AppendMethodHead(method.Name, arity);
            AppendArguments(node.Arguments, 0);
        } else {
            Visit(node.Object);
            AppendMethodHead(method.Name, arity);
            AppendArguments(node.Arguments, 0);
        }

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
                // Trailing ':' prevents node-type token from visually merging with the operand.
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

    /// <summary>Returns the accumulated deterministic string.</summary>
    public override string ToString() { return _builder.ToString(); }

    private void AppendMethodHead(string name, int arity) {
        _builder.Append('.').Append(name).Append(':').Append(arity).Append('(');
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
