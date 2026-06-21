using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Expressions.Cel.Expressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

/// <summary>
///     Splits a CEL filter into a backend-translatable part and a local residual at the top-level
///     conjunction: each <c>&amp;&amp;</c> conjunct that is conservatively translatable pushes; the
///     rest stay local. Disjunctions, negations, and nested expressions push only when fully
///     translatable, so the pushed part is always a weakening of the original. When the backend cannot
///     compose booleans (no <see cref="ExpressionCapabilities.Logical" />) the split degrades to
///     whole-or-nothing, since separate pushed clauses are themselves a conjunction.
/// </summary>
public sealed class CelPushdownPlanner : IExpressionPushdownPlanner
{
    #region IExpressionPushdownPlanner Members

    public string Language => CelLanguage.Name;

    public ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities) {
        if (tree is not CelNode node) {
            throw new ArgumentException("Tree must be a CEL node.", nameof(tree));
        }

        if (!capabilities.Logical) {
            return IsPushable(node, capabilities)
                ? new(node, null)
                : new ExpressionPushdownPlan(null, node);
        }

        var conjuncts = new List<CelNode>();
        Flatten(node, conjuncts);

        var pushed   = new List<CelNode>();
        var residual = new List<CelNode>();
        foreach (var conjunct in conjuncts) {
            (IsPushable(conjunct, capabilities) ? pushed : residual).Add(conjunct);
        }

        if (residual.Count == 0) {
            return new ExpressionPushdownPlan(node, null);
        }

        if (pushed.Count == 0) {
            return new ExpressionPushdownPlan(null, node);
        }

        return new ExpressionPushdownPlan(Rebuild(node, pushed, "\u0001P"), Rebuild(node, residual, "\u0001R"));
    }

    #endregion

    private static void Flatten(CelNode node, List<CelNode> conjuncts) {
        if (node is CelBinary { Operator: "&&" } binary) {
            Flatten(binary.Left, conjuncts);
            Flatten(binary.Right, conjuncts);
        } else {
            conjuncts.Add(node);
        }
    }

    // Rebuilds a sub-filter from a subset of the original conjuncts. The suffix keeps the pushed and
    // residual parts on distinct compile-cache keys (the cache is keyed by CelNode.Source). A single
    // conjunct is wrapped in `&& true` so the rebuilt root is a fresh node whose Source can be set
    // without mutating the shared parsed tree.
    private static CelNode Rebuild(CelNode original, List<CelNode> conjuncts, string suffix) {
        var node = conjuncts.Count == 1
            ? new CelBinary("&&", conjuncts[0], new CelConstant(true))
            : conjuncts.Aggregate((left, right) => new CelBinary("&&", left, right));
        node.Source = original.Source + suffix;
        return node;
    }

    private static bool IsPushable(CelNode node) {
        return node switch {
            CelConstant   => true,
            CelIdentifier => true,
            // Navigation chains can diverge from CEL member/null behaviour under backend SQL logic.
            CelMember => false,
            var _     => false,
        };
    }

    private static bool IsPushable(CelNode node, ExpressionCapabilities caps) {
        return node switch {
            CelConstant              => true,
            CelIdentifier            => true,
            CelMember                => false,
            CelBinary binary         => IsPushable(binary, caps),
            CelUnary unary           => IsPushable(unary, caps),
            CelCall call             => IsPushable(call, caps),
            CelMemberCall memberCall => IsPushable(memberCall, caps),
            CelConditional           => false,
            CelList                  => false,
            CelMap                   => false,
            var _                    => false,
        };
    }

    private static bool IsPushable(CelBinary node, ExpressionCapabilities caps) {
        return node.Operator switch {
            "==" or "!=" or "<" or "<=" or ">" or ">=" => caps.Comparison
                                                                 && IsPushable(node.Left, caps)
                                                                 && IsPushable(node.Right, caps)
                                                                 && HasFlatField(node),
            "&&" or "||"                                    => caps.Logical
                                                                 && IsPushable(node.Left, caps)
                                                                 && IsPushable(node.Right, caps),
            "+" or "-" or "*" or "/" or "%"                => caps.Arithmetic
                                                                 && IsPushable(node.Left, caps)
                                                                 && IsPushable(node.Right, caps)
                                                                 && HasFlatField(node),
            "in"                                             => caps.Membership
                                                                 && IsPushable(node.Left, caps)
                                                                 && IsPushable(node.Right, caps),
            var _                                            => false,
        };
    }

    private static bool IsPushable(CelUnary node, ExpressionCapabilities caps) {
        return node.Operator switch {
            "!"   => caps.Logical && IsPushable(node.Operand, caps),
            "-"   => caps.Arithmetic && IsPushable(node.Operand, caps),
            var _ => false,
        };
    }

    private static bool IsPushable(CelCall node, ExpressionCapabilities caps) {
        return node.Name == "has"
            && caps.Presence
            && node.Args.Count == 1
            && node.Args[0] is CelIdentifier;
    }

    private static bool IsPushable(CelMemberCall node, ExpressionCapabilities caps) {
        return node.Name switch {
            "contains" or "startsWith" or "endsWith" => caps.StringMatch
                                                         && node.Target is CelIdentifier
                                                         && node.Args.All(IsPushable),
            var _                                     => false,
        };
    }

    private static bool HasFlatField(CelBinary node) {
        return HasFlatField(node.Left) || HasFlatField(node.Right);
    }

    private static bool HasFlatField(CelNode node) {
        return node switch {
            CelIdentifier    => true,
            CelBinary binary => HasFlatField(binary),
            CelUnary unary   => HasFlatField(unary.Operand),
            var _            => false,
        };
    }
}
