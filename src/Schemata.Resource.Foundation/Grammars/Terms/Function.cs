using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Terms;

public class Function : IComparable
{
    public Function(TextPosition position, Member member, IReadOnlyCollection<IArg>? args) {
        Position = position;

        Member = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    public Member Member { get; }

    public List<IArg> Args { get; } = [];

    #region IComparable Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression? ToExpression(Container ctx) {
        if (!ctx.AllowFunctions) {
            return null;
        }

        var instance = Member.ToMemberExpression(ctx);

        if (instance is null) {
            return null;
        }

        string? method;
        switch (instance) {
            case ConstantExpression { Value: string @string }:
            {
                instance = Expression.Constant(null);
                method   = @string;
                break;
            }
            case ParameterExpression parameter:
            {
                var last = Member.Fields.LastOrDefault()?.ToExpression(ctx);
                if (last is not ConstantExpression { Value: string @string }) {
                    return null;
                }

                instance = parameter;
                method   = @string;
                break;
            }
            default: return null;
        }

        if (!ctx.Functions.TryGetValue(instance.Type, out var methods)) {
            return null;
        }

        if (!methods.TryGetValue(method, out var allowed)) {
            return null;
        }

        if (!allowed) {
            return null;
        }

        return Expression.Call(instance, method, null, Args.Select(p => p.ToExpression(ctx)!).ToArray());
    }

    #endregion

    public override string ToString() {
        return $"{Member}({string.Join(',', Args)})";
    }
}
