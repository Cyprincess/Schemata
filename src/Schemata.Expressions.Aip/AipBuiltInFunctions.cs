using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip;

/// <summary>
///     Resolves built-in and custom functions used by the AIP compiler.
/// </summary>
public static class AipBuiltInFunctions
{
    /// <summary>
    ///     Resolves a function binding by name from compile options or the built-in AIP function set.
    /// </summary>
    public static ExpressionFunction? Resolve(string name, ExpressionCompileOptions? options) {
        if (options?.Functions.TryGetValue(name, out var custom) is true) {
            return custom;
        }

        return name switch {
            "timestamp" => new(BuildTimestamp),
            "duration"  => new(BuildDuration),
            var _       => null,
        };
    }

    /// <summary>
    ///     Creates a cache-key fragment for option-dependent function bindings.
    /// </summary>
    public static string Fingerprint(ExpressionCompileOptions? options) {
        return ExpressionCompileOptions.Fingerprint(options);
    }

    private static Expression BuildTimestamp(IReadOnlyList<Expression> args) {
        var value = GetStringArg("timestamp", args);
        return Expression.Constant(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static Expression BuildDuration(IReadOnlyList<Expression> args) {
        return Expression.Constant(ParseDuration(GetStringArg("duration", args)));
    }

    private static string GetStringArg(string name, IReadOnlyList<Expression> args) {
        if (args is not [ConstantExpression { Value: string value }]) {
            throw new ParseException($"AIP function '{name}' expects one string literal argument.", default);
        }

        return value;
    }

    private static TimeSpan ParseDuration(string source) {
        var total = TimeSpan.Zero;
        for (var i = 0; i < source.Length;) {
            var start = i;
            while (i < source.Length && char.IsDigit(source[i])) {
                i++;
            }

            if (start == i || i >= source.Length) {
                throw new ParseException($"Invalid AIP duration '{source}'.", default);
            }

            var value = double.Parse(source.AsSpan(start, i - start), CultureInfo.InvariantCulture);
            total += source[i++] switch {
                'h'   => TimeSpan.FromHours(value),
                'm'   => TimeSpan.FromMinutes(value),
                's'   => TimeSpan.FromSeconds(value),
                var _ => throw new ParseException($"Invalid AIP duration '{source}'.", default),
            };
        }

        return total;
    }
}
