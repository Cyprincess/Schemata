using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Aip.Values;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Splits an AIP-160 filter into a backend-translatable part and a local residual at the top-level
///     conjunction: each AND conjunct that is conservatively translatable pushes; the rest stay local.
///     A conjunct's disjunction, negation, or parenthesized sub-filter pushes only when fully
///     translatable, so the pushed part is always a weakening of the original.
/// </summary>
public sealed class AipPushdownPlanner : IExpressionPushdownPlanner
{
    #region IExpressionPushdownPlanner Members

    public string Language => AipLanguage.Name;

    public ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities) {
        if (tree is not Filter filter) {
            throw new ArgumentException("Tree must be an AIP filter.", nameof(tree));
        }

        var pushed   = new List<Factor>();
        var residual = new List<Factor>();
        foreach (var factor in filter.Sequences.SelectMany(sequence => sequence.Factors)) {
            (IsPushable(factor, capabilities) ? pushed : residual).Add(factor);
        }

        if (residual.Count == 0) {
            return new ExpressionPushdownPlan(filter, null);
        }

        if (pushed.Count == 0) {
            return new ExpressionPushdownPlan(null, filter);
        }

        return new ExpressionPushdownPlan(Rebuild(filter, pushed, "\u0001P"), Rebuild(filter, residual, "\u0001R"));
    }

    #endregion

    // Rebuilds a sub-filter from a subset of the original conjuncts. The suffix keeps the pushed and
    // residual parts on distinct compile-cache keys, since the cache is keyed by Filter.Source.
    private static Filter Rebuild(Filter original, List<Factor> factors, string suffix) {
        var position = factors[0].Position;
        return new Filter(position, new Sequence(position, factors), null) { Source = original.Source + suffix };
    }

    private static bool IsPushable(Filter node, ExpressionCapabilities caps) {
        return node.Sequences.All(sequence => IsPushable(sequence, caps));
    }

    private static bool IsPushable(Sequence node, ExpressionCapabilities caps) {
        return node.Factors.All(factor => IsPushable(factor, caps));
    }

    private static bool IsPushable(Factor node, ExpressionCapabilities caps) {
        if (node.Terms.Count > 1 && !caps.Logical) {
            return false;
        }

        return node.Terms.All(term => IsPushable(term, caps));
    }

    private static bool IsPushable(Term node, ExpressionCapabilities caps) {
        if (node.Modifier is not null && !caps.Logical) {
            return false;
        }

        return node.Simple switch {
            Restriction restriction => IsPushable(restriction, caps),
            Filter filter           => IsPushable(filter, caps),
            var _                   => false,
        };
    }

    private static bool IsPushable(Restriction node, ExpressionCapabilities caps) {
        // Only flat fields preserve semantics under three-valued SQL logic. A navigation chain carries
        // AIP null-chain guard behaviour that can diverge from SQL, so it stays local.
        if (node.Comparable is not Member { Fields.Count: 0 }) {
            return false;
        }

        if (node.Comparator is null || node.Arg is null) {
            return caps.Presence;
        }

        if (node.Arg is not Member { Fields.Count: 0 } arg) {
            return false;
        }

        return node.Comparator switch {
            Has                                                              => caps.Presence && caps.Membership,
            Equal or NotEqual when HasWildcard(arg)                          => caps.Comparison && caps.Wildcard,
            Equal or NotEqual                                                => caps.Comparison,
            LessThan or LessThanOrEqual or GreaterThan or GreaterThanOrEqual => caps.Comparison,
            var _                                                            => false,
        };
    }

    private static bool HasWildcard(Member member) {
        return member.Value is Text { Value: var value } && value.Contains('*');
    }
}
