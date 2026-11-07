using System;
using System.Collections;
using System.Reflection;
using Humanizer;
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

    public ExpressionPushdownPlan Plan(IExpressionTree tree, ExpressionCapabilities capabilities, Type elementType) {
        if (tree is not CelNode node) {
            throw new ArgumentException("Tree must be a CEL node.", nameof(tree));
        }

        if (!capabilities.Logical) {
            return IsPushable(node, capabilities) && !RequiresLocal(node, elementType)
                ? new(node, null)
                : new ExpressionPushdownPlan(null, node);
        }

        var conjuncts = new List<CelNode>();
        Flatten(node, conjuncts);

        var pushed   = new List<CelNode>();
        var residual = new List<CelNode>();
        foreach (var conjunct in conjuncts) {
            (IsPushable(conjunct, capabilities) && !RequiresLocal(conjunct, elementType) ? pushed : residual).Add(conjunct);
        }

        if (residual.Count == 0) {
            return new(node, null);
        }

        if (pushed.Count == 0) {
            return new(null, node);
        }

        return new(Rebuild(node, pushed, "\u0001P"), Rebuild(node, residual, "\u0001R"));
    }

    #endregion

    private static bool RequiresLocal(CelNode node, Type elementType) {
        return node switch {
            CelBinary binary => binary.Operator is "==" or "!="
                && (IsCollection(binary.Left, elementType) || IsCollection(binary.Right, elementType))
                || RequiresLocal(binary.Left, elementType) || RequiresLocal(binary.Right, elementType),
            CelUnary unary => RequiresLocal(unary.Operand, elementType),
            _ => false,
        };
    }

    private static bool IsCollection(CelNode node, Type elementType) {
        if (node is CelList or CelMap) return true;
        if (node is not CelIdentifier identifier) return false;
        var member = elementType.GetMember(identifier.Name.Pascalize(), BindingFlags.Instance | BindingFlags.Public).FirstOrDefault();
        var type = member switch { PropertyInfo property => property.PropertyType, FieldInfo field => field.FieldType, _ => null };
        return type is not null && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    private static void Flatten(CelNode node, List<CelNode> conjuncts) {
        if (node is CelBinary { Operator: "&&" } binary) {
            Flatten(binary.Left, conjuncts);
            Flatten(binary.Right, conjuncts);
        } else {
            conjuncts.Add(node);
        }
    }

    // Rebuilds a sub-filter from a subset of the original conjuncts. The suffix keeps the pushed and
    // residual Source annotations distinct; compile-cache identity is the structural fingerprint. A
    // single conjunct is wrapped in `&& true` so the rebuilt root is a fresh node whose Source can
    // be set without mutating the shared parsed tree.
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
            "==" or "!=" => caps.Comparison
                            && node.Left is not CelList and not CelMap
                            && node.Right is not CelList and not CelMap
                            && IsPushable(node.Left, caps)
                            && IsPushable(node.Right, caps)
                            && HasFlatField(node),
            "<" or "<=" or ">" or ">=" => caps.Comparison
                                           && IsPushable(node.Left, caps)
                                           && IsPushable(node.Right, caps)
                                           && HasFlatField(node),
            "&&" or "||"                => caps.Logical
                                           && IsPushable(node.Left, caps)
                                           && IsPushable(node.Right, caps),
            "/" or "%"                  => caps.Arithmetic
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
            && node.Args is [CelIdentifier];
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
