using System;
using System.Collections;
using System.Collections.Generic;

namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Runtime helpers an expression compiler emits calls to when compiling against a dynamic row
///     keyed by source alias rather than a statically-typed context. Centralizing member access and
///     operator semantics keeps dynamic evaluation identical across languages.
/// </summary>
public static class DynamicValues
{
    /// <summary>
    ///     A sentinel returned when a member cannot be resolved against a row, distinct from a
    ///     present null value.
    /// </summary>
    public static readonly object Missing = new MissingSentinel();

    /// <summary>
    ///     Determines whether a value is the <see cref="Missing" /> sentinel.
    /// </summary>
    public static bool IsMissing(object? value) {
        return ReferenceEquals(value, Missing);
    }

    /// <summary>
    ///     Reads a named member from a container, returning <see cref="Missing" /> when the container
    ///     is not a string-keyed map or the key is absent.
    /// </summary>
    public static object? Member(object? container, string name) {
        switch (container) {
            case IReadOnlyDictionary<string, object?> typed:
                return typed.TryGetValue(name, out var value) ? value : Missing;
            case IDictionary<string, object?> mutable:
                return mutable.TryGetValue(name, out var entry) ? entry : Missing;
            case IDictionary untyped:
                return untyped.Contains(name) ? untyped[name] : Missing;
            default:
                return Missing;
        }
    }

    /// <summary>
    ///     Determines whether a value is present and non-null.
    /// </summary>
    public static bool IsPresent(object? value) {
        return !IsMissing(value) && value is not null;
    }

    /// <summary>
    ///     Coerces a value to a boolean, treating missing, null, and non-boolean values as false.
    /// </summary>
    public static bool ToBoolean(object? value) {
        return value is bool flag && flag;
    }

    /// <summary>
    ///     Evaluates a bare presence test: a boolean yields itself; any present non-null value yields
    ///     true; missing or null yields false.
    /// </summary>
    public static bool Truthy(object? value) {
        return value is bool flag ? flag : IsPresent(value);
    }

    /// <summary>
    ///     Compares two values for equality. A missing operand is never equal; two nulls are equal;
    ///     numeric operands compare by value across numeric types.
    /// </summary>
    public static bool Equal(object? left, object? right) {
        if (IsMissing(left) || IsMissing(right)) {
            return false;
        }

        if (left is null || right is null) {
            return left is null && right is null;
        }

        if (TryAsDouble(left, out var l) && TryAsDouble(right, out var r)) {
            return l.Equals(r);
        }

        if (left is string ls && right is string rs) {
            return string.Equals(ls, rs, StringComparison.Ordinal);
        }

        return left.Equals(right);
    }

    /// <summary>
    ///     Negated <see cref="Equal" />, except a missing operand yields false.
    /// </summary>
    public static bool NotEqual(object? left, object? right) {
        if (IsMissing(left) || IsMissing(right)) {
            return false;
        }

        return !Equal(left, right);
    }

    /// <summary>
    ///     Determines whether the left operand orders before the right.
    /// </summary>
    public static bool Less(object? left, object? right) {
        return Compare(left, right) is < 0;
    }

    /// <summary>
    ///     Determines whether the left operand orders before or equal to the right.
    /// </summary>
    public static bool LessOrEqual(object? left, object? right) {
        return Compare(left, right) is <= 0;
    }

    /// <summary>
    ///     Determines whether the left operand orders after the right.
    /// </summary>
    public static bool Greater(object? left, object? right) {
        return Compare(left, right) is > 0;
    }

    /// <summary>
    ///     Determines whether the left operand orders after or equal to the right.
    /// </summary>
    public static bool GreaterOrEqual(object? left, object? right) {
        return Compare(left, right) is >= 0;
    }

    /// <summary>
    ///     Adds two numeric operands, yielding <see cref="Missing" /> when either is not numeric.
    /// </summary>
    public static object? Add(object? left, object? right) {
        return Arithmetic(left, right, static (l, r) => l + r);
    }

    /// <summary>
    ///     Subtracts the right numeric operand from the left, yielding <see cref="Missing" /> when
    ///     either is not numeric.
    /// </summary>
    public static object? Subtract(object? left, object? right) {
        return Arithmetic(left, right, static (l, r) => l - r);
    }

    /// <summary>
    ///     Multiplies two numeric operands, yielding <see cref="Missing" /> when either is not numeric.
    /// </summary>
    public static object? Multiply(object? left, object? right) {
        return Arithmetic(left, right, static (l, r) => l * r);
    }

    /// <summary>
    ///     Divides the left numeric operand by the right, yielding <see cref="Missing" /> when either
    ///     is not numeric.
    /// </summary>
    public static object? Divide(object? left, object? right) {
        return Arithmetic(left, right, static (l, r) => l / r);
    }

    /// <summary>
    ///     Computes the remainder of dividing the left numeric operand by the right, yielding
    ///     <see cref="Missing" /> when either is not numeric.
    /// </summary>
    public static object? Modulo(object? left, object? right) {
        return Arithmetic(left, right, static (l, r) => l % r);
    }

    private static int? Compare(object? left, object? right) {
        if (IsMissing(left) || IsMissing(right) || left is null || right is null) {
            return null;
        }

        if (TryAsDouble(left, out var l) && TryAsDouble(right, out var r)) {
            return l.CompareTo(r);
        }

        if (left is string ls && right is string rs) {
            return string.CompareOrdinal(ls, rs);
        }

        if (left.GetType() == right.GetType() && left is IComparable comparable) {
            return comparable.CompareTo(right);
        }

        return null;
    }

    private static object? Arithmetic(object? left, object? right, Func<double, double, double> op) {
        if (TryAsDouble(left, out var l) && TryAsDouble(right, out var r)) {
            return op(l, r);
        }

        return Missing;
    }

    private static bool TryAsDouble(object? value, out double result) {
        switch (value) {
            case byte b:    result = b;         return true;
            case sbyte b:   result = b;         return true;
            case short s:   result = s;         return true;
            case ushort s:  result = s;         return true;
            case int i:     result = i;         return true;
            case uint i:    result = i;         return true;
            case long l:    result = l;         return true;
            case ulong l:   result = l;         return true;
            case float f:   result = f;         return true;
            case double d:  result = d;         return true;
            case decimal m: result = (double)m; return true;
            default:        result = 0;   return false;
        }
    }

    private sealed class MissingSentinel
    {
        public override string ToString() {
            return "<missing>";
        }
    }
}
