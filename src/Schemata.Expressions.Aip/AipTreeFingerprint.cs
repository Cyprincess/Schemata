using System.Globalization;
using System.Text;
using Parlot;
using Schemata.Expressions.Aip.Expressions;
using Schemata.Expressions.Aip.Values;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Produces the canonical structural text identifying an AIP filter for compile caching. The
///     fingerprint walks every node kind and semantic field — comparators, member paths, sequence,
///     factor, and term ordering, function names and arguments, and literals with their quoted
///     flags — so two trees share a cache entry only when they are structurally identical,
///     independent of the mutable <see cref="Filter.Source" /> annotation.
/// </summary>
internal static class AipTreeFingerprint
{
    /// <summary>
    ///     Returns the exact canonical text for the supplied filter; equal texts imply structurally
    ///     identical filters.
    /// </summary>
    public static string Compute(Filter filter) {
        var builder = new StringBuilder();
        Write(filter, builder);
        return builder.ToString();
    }

    private static void Write(Filter node, StringBuilder builder) {
        builder.Append("(and");
        foreach (var sequence in node.Sequences) {
            Write(sequence, builder);
        }

        builder.Append(')');
    }

    private static void Write(Sequence node, StringBuilder builder) {
        builder.Append("(seq");
        foreach (var factor in node.Factors) {
            Write(factor, builder);
        }

        builder.Append(')');
    }

    private static void Write(Factor node, StringBuilder builder) {
        builder.Append("(or");
        foreach (var term in node.Terms) {
            Write(term, builder);
        }

        builder.Append(')');
    }

    private static void Write(Term node, StringBuilder builder) {
        builder.Append("(term ");
        Append(node.Modifier ?? "none", builder);
        Write(node.Simple, builder);
        builder.Append(')');
    }

    private static void Write(ISimple node, StringBuilder builder) {
        switch (node) {
            case Restriction restriction:
                Write(restriction, builder);
                break;
            case Filter filter:
                Write(filter, builder);
                break;
            default:
                throw new ParseException("Unsupported AIP simple expression.", node.Position);
        }
    }

    private static void Write(Restriction node, StringBuilder builder) {
        builder.Append("(restriction");
        if (node.Comparator is not null && node.Arg is not null) {
            builder.Append(' ');
            Append(node.Comparator switch {
                Operations.Has => "has",
                Operations.Equal => "equal",
                Operations.NotEqual => "not-equal:" + node.Comparator.Type,
                Operations.LessThan => "less:" + node.Comparator.Type,
                Operations.LessThanOrEqual => "less-or-equal:" + node.Comparator.Type,
                Operations.GreaterThan => "greater:" + node.Comparator.Type,
                Operations.GreaterThanOrEqual => "greater-or-equal:" + node.Comparator.Type,
                _ => "custom-binary:" + node.Comparator.Type,
            }, builder);
            Write(node.Comparable, builder);
            Write(node.Arg, builder);
        } else {
            Write(node.Comparable, builder);
        }

        builder.Append(')');
    }

    private static void Write(IComparableArg node, StringBuilder builder) {
        switch (node) {
            case Member member:
                Write(member, builder);
                break;
            case Function function:
                Write(function, builder);
                break;
            default:
                throw new ParseException("Unsupported AIP comparable expression.", node.Position);
        }
    }

    private static void Write(IArg node, StringBuilder builder) {
        switch (node) {
            case IComparableArg comparable:
                Write(comparable, builder);
                break;
            case Filter filter:
                Write(filter, builder);
                break;
            default:
                throw new ParseException("Unsupported AIP argument.", node.Position);
        }
    }

    private static void Write(Function node, StringBuilder builder) {
        builder.Append("(call ");
        Write(node.Member, builder);
        foreach (var arg in node.Args) {
            Write(arg, builder);
        }

        builder.Append(')');
    }

    private static void Write(Member node, StringBuilder builder) {
        builder.Append("(member ");
        Write(node.Value, builder);
        foreach (var field in node.Fields) {
            Write(field, builder);
        }

        builder.Append(')');
    }

    private static void Write(IValue node, StringBuilder builder) {
        switch (node) {
            case Text { IsQuoted: true } quoted:
                builder.Append("(text q ");
                Append(quoted.Value, builder);
                break;
            case Text identifier:
                builder.Append("(text i ");
                Append(identifier.Value, builder);
                break;
            case Integer integer:
                builder.Append("(int ").Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case Number number:
                builder.Append("(num ").Append(number.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case Truth truth:
                builder.Append("(bool ").Append(truth.Value ? "true" : "false");
                break;
            case Null:
                builder.Append("(null");
                break;
            default:
                throw new ParseException("Unsupported AIP value.", node.Position);
        }

        builder.Append(')');
    }

    private static void Write(IField node, StringBuilder builder) {
        switch (node) {
            case Text text:
                builder.Append("(field ");
                Append(text.Value, builder);
                break;
            case Integer integer:
                builder.Append("(index ").Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new ParseException("Unsupported AIP field.", node.Position);
        }

        builder.Append(')');
    }

    // Escaping the structural delimiters keeps the canonical text injective for any literal
    // content, so equal fingerprints imply identical trees without a hash collision class.
    private static void Append(string value, StringBuilder builder) {
        foreach (var c in value) {
            switch (c) {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '(':
                    builder.Append("\\(");
                    break;
                case ')':
                    builder.Append("\\)");
                    break;
                case ' ':
                    builder.Append("\\s");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
    }
}
