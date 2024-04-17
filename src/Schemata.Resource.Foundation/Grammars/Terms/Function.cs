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

    public Expression ToExpression(Container ctx) {
        if (!ctx.AllowFunctions) {
            throw new ParseException("Function not allowed", Position);
        }

        var instance = Member.ToMemberExpression(ctx);

        if (instance is null) {
            throw new ParseException("Except name or instance", Member.Position);
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
                var last = Member.Fields.LastOrDefault();
                if (last?.ToExpression(ctx) is not ConstantExpression { Value: string @string }) {
                    throw new ParseException("Except name", last?.Position ?? Position);
                }

                instance = parameter;
                method   = @string;
                break;
            }
            default:
            {
                throw new ParseException("Except name or instance", Member.Position);
            }
        }

        if (!ctx.Functions.TryGetValue(instance.Type, out var methods)) {
            throw new ParseException($"Function {instance.Type.Name}.{method} not allowed", Position);
        }

        if (!methods.TryGetValue(method, out var allowed)) {
            throw new ParseException($"Function {instance.Type.Name}.{method} not allowed", Position);
        }

        if (!allowed) {
            throw new ParseException($"Function {instance.Type.Name}.{method} not allowed", Position);
        }

        return Expression.Call(instance, method, null, Args.Select(p => p.ToExpression(ctx)!).ToArray());
    }

    #endregion

    public override string ToString() {
        return $"{Member}({string.Join(',', Args)})";
    }
}
