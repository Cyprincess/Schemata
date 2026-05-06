using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Abstractions;
using Schemata.Resource.Foundation.Grammars.Values;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     A function call expression (e.g., <c>contains(field, value)</c>) in the filter grammar.
///     Supports fully qualified two-part names (e.g., <c>time.now</c>) and instance-method
///     style calls (e.g., <c>msg.endsWith(value)</c>) where the first segment becomes
///     the instance expression.
/// </summary>
public class Function : IComparableArg
{
    /// <summary>
    ///     Initializes a new function call.
    /// </summary>
    /// <param name="position">The position in the source text.</param>
    /// <param name="member">The member path forming the function name.</param>
    /// <param name="args">Optional argument tokens.</param>
    public Function(TextPosition position, Member member, IReadOnlyCollection<IArg>? args) {
        Position = position;

        Member = member;

        if (args is not null) {
            Args.AddRange(args);
        }
    }

    /// <summary>
    ///     Gets the member path forming the function name.
    /// </summary>
    public Member Member { get; }

    /// <summary>
    ///     Gets the argument tokens.
    /// </summary>
    public List<IArg> Args { get; } = [];

    #region IComparableArg Members

    public TextPosition Position { get; }

    public bool IsConstant => false;

    public Expression ToExpression(Container ctx) {
        var segments = new List<string>();

        if (Member.Value is Text { Value: { } first }) {
            segments.Add(first);
        }

        foreach (var field in Member.Fields) {
            if (field is Text { Value: { } s }) {
                segments.Add(s);
            }
        }

        if (segments.Count == 0) {
            throw new ParseException("Expect function name", Member.Position);
        }

        var name = string.Join(".", segments);

        if (ctx.Functions.TryGetValue(name, out var function)) {
            var expressions = Args.Select(a => a.ToExpression(ctx)!).ToArray();
            return function.Factory(expressions, ctx);
        }

        if (segments.Count > 1) {
            var method = segments[^1];
            if (ctx.Functions.TryGetValue(method, out function)) {
                var instance    = Member.ToMemberExpression(ctx);
                var expressions = new[] { instance }.Concat(Args.Select(a => a.ToExpression(ctx)!)).ToArray();
                return function.Factory(expressions, ctx);
            }
        }

        throw new ParseException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.ST2007), name),
            Position
        );
    }

    #endregion

    public override string ToString() { return $"{Member}({string.Join(',', Args)})"; }
}
