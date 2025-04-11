using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Schemata.Entity.Cache;

public class Stringizing : ExpressionVisitor
{
    private readonly StringBuilder _builder = new();

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
        _builder.Append(node.Name ?? $"p{node.GetHashCode()}");
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
            default:
                _builder.Append(node.Value);
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
        var method = node.Method.Name;
        var source = node.Object ?? node.Arguments[0];
        var arguments = node.Object != null ? node.Arguments : node.Arguments.Skip(1);

        Visit(source);

        _builder.Append('.').Append(method).Append('(');

        var first = true;
        foreach (var argument in arguments) {
            if (!first) {
                _builder.Append(", ");
            }
            first = false;
            Visit(argument);
        }

        _builder.Append(')');

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node) {
        _builder.Append(node.NodeType switch {
            ExpressionType.Not       => "!",
            ExpressionType.Negate    => "-",
            ExpressionType.UnaryPlus => "+",
            ExpressionType.Quote     => string.Empty,
            var _                    => node.NodeType.ToString(),
        });

        Visit(node.Operand);

        return node;
    }

    public override string ToString() {
        return _builder.ToString();
    }
}
