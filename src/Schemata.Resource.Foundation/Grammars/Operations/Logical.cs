using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Terms;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public abstract class Logical : IToken
{
    public abstract IEnumerable<IToken> Tokens { get; }

    public abstract ExpressionType Operator { get; }

    #region IToken Members

    public abstract TextPosition Position { get; }

    public abstract bool IsConstant { get; }

    public virtual Expression? ToExpression(Container ctx) {
        var first = Tokens.FirstOrDefault();

        var expression = first?.ToExpression(ctx);
        if (expression is null) {
            return null;
        }

        if (expression.Type != typeof(bool)) {
            expression = ToRestrictionExpression(first!, ctx);
        }

        if (expression is null) {
            return null;
        }

        foreach (var token in Tokens.Skip(1)) {
            var right = token.ToExpression(ctx);
            if (right is null) {
                break;
            }

            if (right.Type != typeof(bool)) {
                right = ToRestrictionExpression(token, ctx);
            }

            if (right is null) {
                break;
            }

            expression = Expression.MakeBinary(Operator, expression, right);
        }

        return expression;
    }

    #endregion

    protected virtual Expression? ToRestrictionExpression(IToken token, Container ctx) {
        var member = new Member(token.Position, new Text(token.Position, "q"), null);

        var arg = ToArg(token);
        if (arg is null) {
            throw new ParseException("Expect arg", token.Position);
        }

        var restriction = new Restriction(token.Position, member, (new ExactMatch(token.Position), arg));

        return restriction.ToExpression(ctx);
    }

    protected virtual IArg? ToArg(IToken token) {
        return token switch {
            Filter f                           => f,
            Function f                         => f,
            Member m                           => m,
            Term { Modifier         : null } t => ToArg(t.Simple),
            Restriction { Comparator: null } r => ToArg(r.Comparable),
            var _                              => null,
        };
    }
}
