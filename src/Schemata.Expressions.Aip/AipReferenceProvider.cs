using System.Collections.Generic;
using System.Globalization;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Aip.Values;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

public sealed class AipReferenceProvider : IExpressionReferenceProvider
{
    public ExpressionReferences Analyze(IExpressionTree tree) {
        if (tree is not Filter filter) {
            throw new ExpressionException("Tree must be an AIP filter.");
        }

        var references = new List<ExpressionReference>();
        Collect(filter, references);
        return new ExpressionReferences(references, new ExpressionShape.Scalar());
    }

    private static void Collect(Filter filter, List<ExpressionReference> references) {
        foreach (var sequence in filter.Sequences) {
            Collect(sequence, references);
        }
    }

    private static void Collect(Sequence sequence, List<ExpressionReference> references) {
        foreach (var factor in sequence.Factors) {
            Collect(factor, references);
        }
    }

    private static void Collect(Factor factor, List<ExpressionReference> references) {
        foreach (var term in factor.Terms) {
            Collect(term, references);
        }
    }

    private static void Collect(Term term, List<ExpressionReference> references) {
        Collect(term.Simple, references);
    }

    private static void Collect(ISimple simple, List<ExpressionReference> references) {
        switch (simple) {
            case Restriction restriction:
                CollectRestriction(restriction, references);
                break;
            case Filter nested:
                Collect(nested, references);
                break;
        }
    }

    private static void CollectRestriction(Restriction restriction, List<ExpressionReference> references) {
        CollectLhs(restriction, references);
        if (restriction.Comparator is Equal && restriction.Arg is Member { Value: Text { IsQuoted: true }, Fields.Count: 0 }) return;
        if (restriction.Arg is not null) {
            CollectRhs(restriction.Arg, references);
        }
    }

    private static void CollectLhs(Restriction restriction, List<ExpressionReference> references) {
        if (restriction.Comparable is Member member) {
            CollectLhsMember(member, references, restriction.Comparator);
            return;
        }

        if (restriction.Comparable is Function function) {
            CollectFunctionArgs(function, references);
        }
    }

    private static void CollectRhs(IArg arg, List<ExpressionReference> references) {
        switch (arg) {
            case IComparableArg comparable:
                CollectRhsComparable(comparable, references);
                break;
            case Filter nested:
                Collect(nested, references);
                break;
        }
    }

    private static void CollectRhsComparable(IComparableArg comparable, List<ExpressionReference> references) {
        switch (comparable) {
            case Member member:
                CollectRhsMember(member, references);
                break;
            case Function function:
                CollectFunctionArgs(function, references);
                break;
        }
    }

    private static void CollectFunctionArgs(Function function, List<ExpressionReference> references) {
        foreach (var arg in function.Args) {
            CollectRhs(arg, references);
        }
    }

    private static void CollectLhsMember(Member member, List<ExpressionReference> references, IBinary? comparator) {
        // Query admission requires a public left operand even where the compiler accepts a literal comparison.
        if (member.Value is not Text text) {
            return;
        }

        var repeated = comparator is Has;
        EmitPath(text.Value, member.Fields, mayBeLiteral: false, repeated, references);
    }

    private static void CollectRhsMember(Member member, List<ExpressionReference> references) {
        if (member.Value is not Text text) {
            return;
        }

        EmitPath(text.Value, member.Fields, mayBeLiteral: true, repeated: false, references);
    }

    private static void EmitPath(
        string                     root,
        IReadOnlyList<IField>      fields,
        bool                       mayBeLiteral,
        bool                       repeated,
        List<ExpressionReference>  references
    ) {
        var path = new List<string> { root };
        foreach (var field in fields) {
            switch (field) {
                case Text textField:
                    path.Add(textField.Value);
                    break;
                case Integer integerField:
                    path.Add(integerField.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ExpressionException("Unsupported AIP field.");
            }
        }

        references.Add(new ExpressionReference(path, MayBeLiteral: mayBeLiteral, Repeated: repeated));
    }
}