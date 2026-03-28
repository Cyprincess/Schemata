using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Abstractions;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Represents a function call expression (e.g. <c>contains(field, value)</c>) in the filter grammar.
/// </summary>
public class Function : IComparableArg
{
    public Function(TextPosition position, Member member, IReadOnlyCollection<IArg>? args) {
        Position = position;

        Member = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    /// <summary>
    ///     Gets the member path that forms the function name.
    /// </summary>
    public Member Member { get; }

    /// <summary>
    ///     Gets the list of arguments passed to the function.
    /// </summary>
    public List<IArg> Args { get; } = [];

    #region IComparable Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression ToExpression(Container ctx) {
        var segments = new List<string>();

        if (Member.Value is Text { Value: string first }) {
            segments.Add(first);
        }

        foreach (var field in Member.Fields) {
            if (field is Text { Value: string s }) {
                segments.Add(s);
            }
        }

        if (segments.Count == 0) {
            throw new ParseException("Expect function name", Member.Position);
        }

        var name = string.Join(".", segments);

        // Try full name lookup (e.g., "time.now", "math.mem", "regex")
        if (ctx.Functions.TryGetValue(name, out var function)) {
            var expressions = Args.Select(a => a.ToExpression(ctx)!).ToArray();
            return function.Factory(expressions, ctx);
        }

        // Try last segment as function name (instance-style, e.g., "msg.endsWith")
        if (segments.Count > 1) {
            var method = segments[^1];
            if (ctx.Functions.TryGetValue(method, out function)) {
                var instance    = Member.ToMemberExpression(ctx);
                var expressions = new[] { instance }.Concat(Args.Select(a => a.ToExpression(ctx)!)).ToArray();
                return function.Factory(expressions, ctx);
            }
        }

        throw new ParseException(string.Format(SchemataResources.GetResourceString(SchemataResources.ST2007), name), Position);
    }

    #endregion

    public override string ToString() { return $"{Member}({string.Join(',', Args)})"; }
}
