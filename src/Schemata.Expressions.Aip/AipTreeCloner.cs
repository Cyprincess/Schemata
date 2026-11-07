using System;
using Parlot;
using Schemata.Expressions.Aip.Expressions;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Creates independent copies of mutable AIP syntax trees.
/// </summary>
internal static class AipTreeCloner
{
    public static Filter Clone(Filter source) {
        var clone = new Filter(source.Position, Clone(source.Sequences[0]), null) {
            Source = source.Source,
        };
        for (var i = 1; i < source.Sequences.Count; i++) {
            clone.Sequences.Add(Clone(source.Sequences[i]));
        }

        return clone;
    }

    private static Sequence Clone(Sequence source) {
        var clone = new Sequence(source.Position, Array.Empty<Factor>());
        foreach (var factor in source.Factors) {
            clone.Factors.Add(Clone(factor));
        }

        return clone;
    }

    private static Factor Clone(Factor source) {
        var clone = new Factor(source.Position, Clone(source.Terms[0]), null);
        for (var i = 1; i < source.Terms.Count; i++) {
            clone.Terms.Add(Clone(source.Terms[i]));
        }

        return clone;
    }

    private static Term Clone(Term source) {
        return new(source.Position, source.Modifier, Clone(source.Simple));
    }

    private static ISimple Clone(ISimple source) {
        return source switch {
            Restriction restriction => Clone(restriction),
            Filter filter           => Clone(filter),
            var _                   => throw new ParseException("Unsupported AIP simple expression.", source.Position),
        };
    }

    private static Restriction Clone(Restriction source) {
        var comparable = Clone(source.Comparable);
        if (source.Comparator is { } comparator && source.Arg is { } arg) {
            return new(source.Position, comparable, (comparator, Clone(arg)));
        }

        return new(source.Position, comparable, null);
    }

    private static IComparableArg Clone(IComparableArg source) {
        return source switch {
            Member member     => Clone(member),
            Function function => Clone(function),
            var _             => throw new ParseException("Unsupported AIP comparable expression.", source.Position),
        };
    }

    private static IArg Clone(IArg source) {
        return source switch {
            Member member     => Clone(member),
            Function function => Clone(function),
            Filter filter     => Clone(filter),
            var _             => throw new ParseException("Unsupported AIP argument.", source.Position),
        };
    }

    private static Member Clone(Member source) {
        var clone = new Member(source.Position, source.Value, null);
        clone.Fields.AddRange(source.Fields);
        return clone;
    }

    private static Function Clone(Function source) {
        var clone = new Function(source.Position, Clone(source.Member), null);
        foreach (var arg in source.Args) {
            clone.Args.Add(Clone(arg));
        }

        return clone;
    }
}
