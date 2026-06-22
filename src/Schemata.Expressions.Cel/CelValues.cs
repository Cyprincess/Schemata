using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel;

public static class CelValues
{
    public static readonly IEqualityComparer<object> KeyComparer = new CelKeyComparer();

    public static object? Member(object? container, string name) {
        if (container is CelError error) {
            return error;
        }

        switch (container) {
            case IReadOnlyDictionary<string, object?> typed:
                return typed.TryGetValue(name, out var value) ? Normalize(value) : DynamicValues.Missing;
            case IDictionary<string, object?> mutable:
                return mutable.TryGetValue(name, out var entry) ? Normalize(entry) : DynamicValues.Missing;
            case IDictionary untyped:
                return untyped.Contains(name) ? Normalize(untyped[name]) : DynamicValues.Missing;
            case CelTimestamp timestamp:
                return TimestampMember(timestamp, name, null);
            case CelDuration duration:
                return DurationMember(duration, name);
            default:
                return DynamicValues.Missing;
        }
    }

    public static object? Identifier(object? container, string name) {
        var value = Member(container, name);
        return DynamicValues.IsMissing(value) ? Error($"undeclared reference to '{name}' (in container '')") : value;
    }

    public static bool IsTrue(object? value) { return value is true; }

    public static bool IsFalse(object? value) { return value is false; }

    public static object? PredicateResult(object? value) { return value is true; }

    public static object? ConditionalError(object? value) {
        return value is CelError error ? error : Error("no matching overload");
    }

    public static object? Has(object? value) {
        return value is CelError ? value : !DynamicValues.IsMissing(value);
    }

    public static object? Not(object? value) {
        return value switch {
            CelError => value,
            bool flag => !flag,
            _ => Error("no matching overload"),
        };
    }

    public static object? Negate(object? value) {
        try {
            return value switch {
                CelError => value,
                long i => checked(-i),
                ulong => Error("no matching overload"),
                double d => -d,
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? And(object? left, object? right) {
        if (left is false || right is false) {
            return false;
        }

        if (left is true && right is true) {
            return true;
        }

        if (left is CelError le) {
            return le;
        }

        if (right is CelError re) {
            return re;
        }

        return Error("no matching overload");
    }

    public static object? Or(object? left, object? right) {
        if (left is true || right is true) {
            return true;
        }

        if (left is false && right is false) {
            return false;
        }

        if (left is CelError le) {
            return le;
        }

        if (right is CelError re) {
            return re;
        }

        return Error("no matching overload");
    }

    public static object? Add(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        try {
            return (left, right) switch {
                (long l, long r) => checked(l + r),
                (ulong l, ulong r) => checked(l + r),
                (double l, double r) => l + r,
                (CelTimestamp l, CelDuration r) => AddDuration(l, r),
                (CelDuration l, CelTimestamp r) => AddDuration(r, l),
                (CelDuration l, CelDuration r) => DurationFromNanos(checked(ToNanos(l) + ToNanos(r))),
                (string l, string r) => l + r,
                (byte[] l, byte[] r) => l.Concat(r).ToArray(),
                (IReadOnlyList<object?> l, IReadOnlyList<object?> r) => l.Concat(r).ToList(),
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? Subtract(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        try {
            return (left, right) switch {
                (long l, long r) => checked(l - r),
                (ulong l, ulong r) => checked(l - r),
                (double l, double r) => l - r,
                (CelTimestamp l, CelTimestamp r) => DurationFromNanos(checked(ToNanos(l) - ToNanos(r))),
                (CelTimestamp l, CelDuration r) => AddDuration(l, new CelDuration(-r.Seconds, -r.Nanos)),
                (CelDuration l, CelDuration r) => DurationFromNanos(checked(ToNanos(l) - ToNanos(r))),
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? Multiply(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        try {
            return (left, right) switch {
                (long l, long r) => checked(l * r),
                (ulong l, ulong r) => checked(l * r),
                (double l, double r) => l * r,
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? Divide(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        try {
            return (left, right) switch {
                (long, 0L) => Error("divide by zero"),
                (ulong, 0UL) => Error("divide by zero"),
                (long l, long r) => checked(l / r),
                (ulong l, ulong r) => checked(l / r),
                (double l, double r) => l / r,
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? Modulo(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        try {
            return (left, right) switch {
                (long, 0L) => Error("modulus by zero"),
                (ulong, 0UL) => Error("modulus by zero"),
                (long l, long r) => checked(l % r),
                (ulong l, ulong r) => checked(l % r),
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("overflow");
        }
    }

    public static object? Equal(object? left, object? right) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        return EqualValue(left, right);
    }

    public static object? NotEqual(object? left, object? right) {
        var equal = Equal(left, right);
        return equal is CelError ? equal : !(bool)equal!;
    }

    public static object? Less(object? left, object? right) { return CompareResult(left, right, c => c < 0); }

    public static object? LessOrEqual(object? left, object? right) { return CompareResult(left, right, c => c <= 0); }

    public static object? Greater(object? left, object? right) { return CompareResult(left, right, c => c > 0); }

    public static object? GreaterOrEqual(object? left, object? right) { return CompareResult(left, right, c => c >= 0); }

    public static object? Contains(object? source, object? value) {
        if (FirstError(source, value) is { } error) {
            return error;
        }

        if (source is string text && value is string fragment) {
            return text.Contains(fragment, StringComparison.Ordinal);
        }

        if (source is IReadOnlyList<object?> list) {
            foreach (var item in list) {
                var equal = Equal(item, value);
                if (equal is CelError e) {
                    return e;
                }

                if (equal is true) {
                    return true;
                }
            }

            return false;
        }

        if (source is IReadOnlyDictionary<object, object?> map) {
            return map.ContainsKey(value!);
        }

        return Error("no matching overload");
    }

    public static object? Index(object? source, object? index) {
        if (FirstError(source, index) is { } error) {
            return error;
        }

        if (source is IReadOnlyList<object?> list) {
            if (!TryIndex(index, list.Count, out var i)) {
                return Error("invalid_argument");
            }

            return list[i];
        }

        if (source is string text) {
            var runes = text.EnumerateRunes().ToList();
            if (!TryIndex(index, runes.Count, out var i)) {
                return Error("invalid_argument");
            }

            return runes[i].ToString();
        }

        if (source is IReadOnlyDictionary<object, object?> map) {
            return map.TryGetValue(index!, out var value) ? value : Error("no such key");
        }

        return Error("no matching overload");
    }

    public static object? List(params object?[] values) {
        return values.Any(IsError) ? values.First(IsError) : values.ToList();
    }

    public static object? Map(params object?[] entries) {
        var map = new Dictionary<object, object?>(KeyComparer);
        for (var i = 0; i < entries.Length; i += 2) {
            if (FirstError(entries[i], entries[i + 1]) is { } error) {
                return error;
            }

            map[entries[i]!] = entries[i + 1];
        }

        return map;
    }

    public static object? Size(object? value) {
        return value switch {
            CelError => value,
            string text => (long)CountRunes(text),
            byte[] bytes => (long)bytes.Length,
            IReadOnlyList<object?> list => (long)list.Count,
            IReadOnlyDictionary<object, object?> map => (long)map.Count,
            _ => Error("no matching overload"),
        };
    }

    private static int CountRunes(string text) {
        var count = 0;
        foreach (var _ in text.EnumerateRunes()) {
            count++;
        }

        return count;
    }

    public static object? StartsWith(object? value, object? prefix) {
        return StringBinary(value, prefix, static (s, a) => s.StartsWith(a, StringComparison.Ordinal));
    }

    public static object? EndsWith(object? value, object? suffix) {
        return StringBinary(value, suffix, static (s, a) => s.EndsWith(a, StringComparison.Ordinal));
    }

    public static object? Matches(object? value, object? pattern) {
        if (FirstError(value, pattern) is { } error) {
            return error;
        }

        if (value is not string text || pattern is not string regex) {
            return Error("no matching overload");
        }

        try {
            return Regex.IsMatch(text, regex, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        } catch (ArgumentException ex) {
            return Error(ex.Message);
        } catch (RegexMatchTimeoutException ex) {
            return Error(ex.Message);
        }
    }

    public static object? Convert(string name, object? value) {
        if (value is CelError) {
            return value;
        }

        return name switch {
            "dyn" => value,
            "type" => TypeOf(value),
            "int" => ToInt(value),
            "uint" => ToUInt(value),
            "double" => ToDouble(value),
            "string" => ToStringValue(value),
            "bytes" => ToBytes(value),
            "bool" => ToBool(value),
            "timestamp" => ToTimestamp(value),
            "duration" => ToDuration(value),
            _ => Error("no matching overload"),
        };
    }

    public static object? Call(string name, object? target, params object?[] args) {
        if (target is CelError) {
            return target;
        }

        if (args.FirstOrDefault(IsError) is { } error) {
            return error;
        }

        return name switch {
            "contains" when args.Length == 1 => Contains(target, args[0]),
            "startsWith" when args.Length == 1 => StartsWith(target, args[0]),
            "endsWith" when args.Length == 1 => EndsWith(target, args[0]),
            "matches" when args.Length == 1 => Matches(target, args[0]),
            "size" when args.Length == 0 => Size(target),
            "getFullYear" => TimeMember(target, "getFullYear", args),
            "getMonth" => TimeMember(target, "getMonth", args),
            "getDate" => TimeMember(target, "getDate", args),
            "getDayOfMonth" => TimeMember(target, "getDayOfMonth", args),
            "getDayOfWeek" => TimeMember(target, "getDayOfWeek", args),
            "getDayOfYear" => TimeMember(target, "getDayOfYear", args),
            "getHours" => TimeMember(target, "getHours", args),
            "getMinutes" => TimeMember(target, "getMinutes", args),
            "getSeconds" => TimeMember(target, "getSeconds", args),
            "getMilliseconds" => TimeMember(target, "getMilliseconds", args),
            _ => Error("no matching overload"),
        };
    }

    private static object? TimeMember(object? target, string name, object?[] args) {
        return target switch {
            CelDuration duration when args.Length == 0 => DurationMember(duration, name),
            CelTimestamp => TimestampMember(target, name, args),
            _ => Error("no matching overload"),
        };
    }

    public static object? UnknownFunction(string name) { return Error("unbound function"); }

    private static object? Normalize(object? value) {
        return value switch {
            int i => (long)i,
            short i => (long)i,
            sbyte i => (long)i,
            byte i => (long)i,
            uint u => (ulong)u,
            ushort u => (ulong)u,
            float f => (double)f,
            IDictionary<string, object?> map => map.ToDictionary(pair => (object)pair.Key, pair => Normalize(pair.Value), KeyComparer),
            IReadOnlyDictionary<string, object?> map => map.ToDictionary(pair => (object)pair.Key, pair => Normalize(pair.Value), KeyComparer),
            IEnumerable<object?> list when value is not string and not byte[] => list.Select(Normalize).ToList(),
            var other => other,
        };
    }

    public static object? Exists(object? source, Func<object?, object?> predicate) {
        if (source is CelError) {
            return source;
        }

        var error = (CelError?)null;
        foreach (var item in Iterate(source)) {
            var result = predicate(item);
            if (result is true) {
                return true;
            }

            if (result is CelError e && error is null) {
                error = e;
            }
        }

        return error is null ? false : error;
    }

    public static object? All(object? source, Func<object?, object?> predicate) {
        if (source is CelError) {
            return source;
        }

        var error = (CelError?)null;
        foreach (var item in Iterate(source)) {
            var result = predicate(item);
            if (result is false) {
                return false;
            }

            if (result is CelError e && error is null) {
                error = e;
            }
        }

        return error is null ? true : error;
    }

    public static object? ExistsOne(object? source, Func<object?, object?> predicate) {
        if (source is CelError) {
            return source;
        }

        var count = 0;
        foreach (var item in Iterate(source)) {
            var result = predicate(item);
            if (result is CelError) {
                return result;
            }

            if (result is true) {
                count++;
            }
        }

        return count == 1;
    }

    public static object? Filter(object? source, Func<object?, object?> predicate) {
        if (source is CelError) {
            return source;
        }

        var result = new List<object?>();
        foreach (var item in Iterate(source)) {
            var include = predicate(item);
            if (include is CelError) {
                return include;
            }

            if (include is true) {
                result.Add(item);
            }
        }

        return result;
    }

    public static object? MapMacro(object? source, Func<object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new List<object?>();
        foreach (var item in Iterate(source)) {
            var transformed = transform(item);
            if (transformed is CelError) {
                return transformed;
            }

            result.Add(transformed);
        }

        return result;
    }

    public static object? MapMacro(object? source, Func<object?, object?> predicate, Func<object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new List<object?>();
        foreach (var item in Iterate(source)) {
            var include = predicate(item);
            if (include is CelError) {
                return include;
            }

            if (include is not true) {
                continue;
            }

            var transformed = transform(item);
            if (transformed is CelError) {
                return transformed;
            }

            result.Add(transformed);
        }

        return result;
    }

    public static object? Exists2(object? source, Func<object?, object?, object?> predicate) {
        return Scan2(source, (i, v) => predicate(i, v), "exists");
    }

    public static object? All2(object? source, Func<object?, object?, object?> predicate) {
        return Scan2(source, (i, v) => predicate(i, v), "all");
    }

    public static object? ExistsOne2(object? source, Func<object?, object?, object?> predicate) {
        if (source is CelError) {
            return source;
        }

        var count = 0;
        foreach (var (key, value) in Iterate2(source)) {
            var result = predicate(key, value);
            if (result is CelError) {
                return result;
            }

            if (result is true) {
                count++;
            }
        }

        return count == 1;
    }

    public static object? TransformList2(object? source, Func<object?, object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new List<object?>();
        foreach (var (key, item) in Iterate2(source)) {
            var transformed = transform(key, item);
            if (transformed is CelError) {
                return transformed;
            }

            result.Add(transformed);
        }

        return result;
    }

    public static object? TransformList2(object? source, Func<object?, object?, object?> predicate, Func<object?, object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new List<object?>();
        foreach (var (key, item) in Iterate2(source)) {
            var include = predicate(key, item);
            if (include is CelError) {
                return include;
            }

            if (include is not true) {
                continue;
            }

            var transformed = transform(key, item);
            if (transformed is CelError) {
                return transformed;
            }

            result.Add(transformed);
        }

        return result;
    }

    public static object? TransformMap2(object? source, Func<object?, object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new Dictionary<object, object?>(KeyComparer);
        foreach (var (key, value) in Iterate2(source)) {
            var transformed = transform(key, value);
            if (transformed is CelError) {
                return transformed;
            }

            result[key!] = transformed;
        }

        return result;
    }

    public static object? TransformMap2(object? source, Func<object?, object?, object?> predicate, Func<object?, object?, object?> transform) {
        if (source is CelError) {
            return source;
        }

        var result = new Dictionary<object, object?>(KeyComparer);
        foreach (var (key, value) in Iterate2(source)) {
            var include = predicate(key, value);
            if (include is CelError) {
                return include;
            }

            if (include is not true) {
                continue;
            }

            var transformed = transform(key, value);
            if (transformed is CelError) {
                return transformed;
            }

            result[key!] = transformed;
        }

        return result;
    }

    private static object? Scan2(object? source, Func<object?, object?, object?> predicate, string mode) {
        if (source is CelError) {
            return source;
        }

        var error = (CelError?)null;
        foreach (var (key, value) in Iterate2(source)) {
            var result = predicate(key, value);
            if (mode == "exists" && result is true) {
                return true;
            }

            if (mode == "all" && result is false) {
                return false;
            }

            if (result is CelError e && error is null) {
                error = e;
            }
        }

        return mode == "exists"
            ? error is null ? false : error
            : error is null ? true : error;
    }

    private static object? CompareResult(object? left, object? right, Func<int, bool> op) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        var comparison = Compare(left, right);
        return comparison.HasValue ? op(comparison.Value) : Error("no matching overload");
    }

    private static int? Compare(object? left, object? right) {
        if (TryCompareNumbers(left, right, out var numeric)) {
            return numeric;
        }

        return (left, right) switch {
            (string l, string r) => string.CompareOrdinal(l, r),
            (byte[] l, byte[] r) => CompareBytes(l, r),
            (bool l, bool r) => l.CompareTo(r),
            (CelTimestamp l, CelTimestamp r) => ToNanos(l).CompareTo(ToNanos(r)),
            (CelDuration l, CelDuration r) => ToNanos(l).CompareTo(ToNanos(r)),
            _ => null,
        };
    }

    private static bool EqualValue(object? left, object? right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }

        if (left is null || right is null || DynamicValues.IsMissing(left) || DynamicValues.IsMissing(right)) {
            return false;
        }

        if (IsNumber(left) && IsNumber(right)) {
            if (left is double || right is double) {
                var lv = ToDoubleNumber(left);
                var rv = ToDoubleNumber(right);
                return !double.IsNaN(lv) && !double.IsNaN(rv) && lv == rv;
            }

            if (TryCompareNumbers(left, right, out var numeric)) {
                return numeric == 0;
            }
        }

        if (left is byte[] lb && right is byte[] rb) {
            return lb.SequenceEqual(rb);
        }

        if (left is IReadOnlyList<object?> ll && right is IReadOnlyList<object?> rl) {
            return ll.Count == rl.Count && ll.Zip(rl).All(pair => EqualValue(pair.First, pair.Second));
        }

        if (left is IReadOnlyDictionary<object, object?> lm && right is IReadOnlyDictionary<object, object?> rm) {
            return lm.Count == rm.Count
                && lm.All(kv => rm.TryGetValue(kv.Key, out var value) && EqualValue(kv.Value, value));
        }

        return left.GetType() == right.GetType() && left.Equals(right);
    }

    private static bool TryCompareNumbers(object? left, object? right, out int comparison) {
        comparison = 0;
        if (!IsNumber(left) || !IsNumber(right)) {
            return false;
        }

        if (left is double ld || right is double) {
            var l = ToDoubleNumber(left);
            var r = ToDoubleNumber(right);
            comparison = l.CompareTo(r);
            return true;
        }

        if (left is ulong lu && right is long rs) {
            comparison = rs < 0 ? 1 : lu.CompareTo((ulong)rs);
            return true;
        }

        if (left is long ls && right is ulong ru) {
            comparison = ls < 0 ? -1 : ((ulong)ls).CompareTo(ru);
            return true;
        }

        comparison = left switch {
            long l when right is long r => l.CompareTo(r),
            ulong l when right is ulong r => l.CompareTo(r),
            _ => 0,
        };
        return true;
    }

    private static object? TypeOf(object? value) {
        return value switch {
            null => new CelType("null_type"),
            bool => new CelType("bool"),
            long => new CelType("int"),
            ulong => new CelType("uint"),
            double => new CelType("double"),
            string => new CelType("string"),
            byte[] => new CelType("bytes"),
            IReadOnlyList<object?> => new CelType("list"),
            IReadOnlyDictionary<object, object?> => new CelType("map"),
            CelType => new CelType("type"),
            CelTimestamp => new CelType("google.protobuf.Timestamp"),
            CelDuration => new CelType("google.protobuf.Duration"),
            _ => new CelType("dyn"),
        };
    }

    private static object? ToInt(object? value) {
        try {
            return value switch {
                long i => i,
                ulong u when u <= long.MaxValue => (long)u,
                ulong => Error("range error"),
                double d when double.IsNaN(d) || double.IsInfinity(d) => Error("range error"),
                double d when d >= 9223372036854775808.0 || d <= -9223372036854775808.0 => Error("range error"),
                double d => (long)d,
                string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
                CelTimestamp ts => ts.Seconds,
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("range error");
        }
    }

    private static object? ToUInt(object? value) {
        try {
            return value switch {
                ulong u => u,
                long i when i >= 0 => (ulong)i,
                long => Error("range error"),
                double d when double.IsNaN(d) || double.IsInfinity(d) => Error("range error"),
                double d when d < 0 || d >= ulong.MaxValue => Error("range error"),
                double d => (ulong)d,
                string s when ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) => u,
                _ => Error("no matching overload"),
            };
        } catch (OverflowException) {
            return Error("range error");
        }
    }

    private static object? ToDouble(object? value) {
        return value switch {
            double d => d,
            long i => (double)i,
            ulong u => (double)u,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => Error("no matching overload"),
        };
    }

    private static object? ToStringValue(object? value) {
        return value switch {
            string s => s,
            long i => i.ToString(CultureInfo.InvariantCulture),
            ulong u => u.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G15", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            byte[] bytes => DecodeUtf8(bytes),
            CelType type => type.Name,
            CelTimestamp ts => FormatTimestamp(ts),
            CelDuration d => FormatDuration(d),
            _ => Error("no matching overload"),
        };
    }

    private static object? ToBytes(object? value) {
        return value switch {
            string s => Encoding.UTF8.GetBytes(s),
            byte[] bytes => bytes,
            _ => Error("no matching overload"),
        };
    }

    private static object? ToBool(object? value) {
        if (value is bool) {
            return value;
        }

        if (value is not string text) {
            return Error("no matching overload");
        }

        return text switch {
            "1" or "t" or "T" or "true" or "True" or "TRUE" => true,
            "0" or "f" or "F" or "false" or "False" or "FALSE" => false,
            var _ => Error("no matching overload"),
        };
    }

    private static object? ToTimestamp(object? value) {
        if (value is CelTimestamp) {
            return value;
        }

        if (value is long seconds) {
            return new CelTimestamp(seconds, 0);
        }

        if (value is not string text) {
            return Error("no matching overload");
        }

        var normalized = Regex.Replace(text, "\\.[0-9]+", string.Empty);
        if (!DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) {
            return Error("invalid timestamp");
        }

        var nanos = ParseFractionNanos(text);
        var parsedSeconds = dto.ToUnixTimeSeconds();
        return new CelTimestamp(parsedSeconds, nanos);
    }

    private static object? ToDuration(object? value) {
        if (value is CelDuration) {
            return value;
        }

        if (value is not string text) {
            return Error("no matching overload");
        }

        return ParseDuration(text);
    }

    private static object? TimestampMember(object? target, string name, object?[]? args) {
        if (target is not CelTimestamp timestamp) {
            return Error("no matching overload");
        }

        var zone = args is { Length: 1 } ? args[0] as string : null;
        if (args is { Length: > 1 } || (args is { Length: 1 } && zone is null)) {
            return Error("no matching overload");
        }

        var local = ToLocalTime(timestamp, zone);
        return name switch {
            "getFullYear" => (long)local.Year,
            "getMonth" => (long)(local.Month - 1),
            "getDate" => (long)local.Day,
            "getDayOfMonth" => (long)(local.Day - 1),
            "getDayOfWeek" => (long)local.DayOfWeek,
            "getDayOfYear" => (long)(local.DayOfYear - 1),
            "getHours" => (long)local.Hour,
            "getMinutes" => (long)local.Minute,
            "getSeconds" => (long)local.Second,
            "getMilliseconds" => (long)(timestamp.Nanos / 1_000_000),
            _ => Error("no matching overload"),
        };
    }

    private static object? DurationMember(CelDuration duration, string name) {
        return name switch {
            "getHours" => duration.Seconds / 3600,
            "getMinutes" => duration.Seconds / 60,
            "getSeconds" => duration.Seconds,
            "getMilliseconds" => (long)(duration.Nanos / 1_000_000),
            _ => DynamicValues.Missing,
        };
    }

    private static DateTimeOffset ToLocalTime(CelTimestamp timestamp, string? zone) {
        var dto = DateTimeOffset.FromUnixTimeSeconds(timestamp.Seconds).AddTicks(timestamp.Nanos / 100);
        if (string.IsNullOrEmpty(zone) || zone is "UTC" or "Z") {
            return dto.ToUniversalTime();
        }

        if (TimeSpan.TryParse(zone.StartsWith("+", StringComparison.Ordinal) ? zone.Substring(1) : zone, CultureInfo.InvariantCulture, out var offset)) {
            if (zone.StartsWith("-", StringComparison.Ordinal) && offset > TimeSpan.Zero) {
                offset = -offset;
            }

            return dto.ToOffset(offset);
        }

        try {
            return TimeZoneInfo.ConvertTime(dto, TimeZoneInfo.FindSystemTimeZoneById(zone));
        } catch (TimeZoneNotFoundException) {
            return dto.ToUniversalTime();
        } catch (InvalidTimeZoneException) {
            return dto.ToUniversalTime();
        }
    }

    private static IEnumerable<object?> Iterate(object? source) {
        return source switch {
            IReadOnlyDictionary<object, object?> map => map.Keys.Cast<object?>(),
            IReadOnlyList<object?> list => list,
            IEnumerable enumerable when source is not string => enumerable.Cast<object?>(),
            _ => [Error("no matching overload")],
        };
    }

    private static IEnumerable<(object? Key, object? Value)> Iterate2(object? source) {
        switch (source) {
            case IReadOnlyDictionary<object, object?> map:
                foreach (var pair in map) {
                    yield return (pair.Key, pair.Value);
                }

                break;
            case IReadOnlyList<object?> list:
                for (var i = 0; i < list.Count; i++) {
                    yield return ((long)i, list[i]);
                }

                break;
            default:
                yield return (0L, Error("no matching overload"));
                break;
        }
    }

    private static bool TryIndex(object? value, int count, out int index) {
        index = value switch {
            long i when i >= 0 && i < count => (int)i,
            ulong i when i < (ulong)count => (int)i,
            double d when d % 1 == 0 && d >= 0 && d < count => (int)d,
            _ => -1,
        };

        return index >= 0;
    }

    private static object? StringBinary(object? left, object? right, Func<string, string, bool> op) {
        if (FirstError(left, right) is { } error) {
            return error;
        }

        return left is string l && right is string r ? op(l, r) : Error("no matching overload");
    }

    private static CelError Error(string message) { return new(message); }

    private static bool IsError(object? value) { return value is CelError; }

    private static CelError? FirstError(object? left, object? right) {
        return left as CelError ?? right as CelError;
    }

    private static bool IsNumber(object? value) { return value is long or ulong or double; }

    private static double ToDoubleNumber(object? value) {
        return value switch {
            long i => i,
            ulong u => u,
            double d => d,
            _ => 0,
        };
    }

    private static int CompareBytes(byte[] left, byte[] right) {
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++) {
            var comparison = left[i].CompareTo(right[i]);
            if (comparison != 0) {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static IEnumerable<string> TextElements(string text) {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) {
            yield return enumerator.GetTextElement();
        }
    }

    private static object? DecodeUtf8(byte[] bytes) {
        try {
            return new UTF8Encoding(false, true).GetString(bytes);
        } catch (DecoderFallbackException ex) {
            return Error(ex.Message);
        }
    }

    private static long ToNanos(CelTimestamp timestamp) {
        return checked(timestamp.Seconds * 1_000_000_000L + timestamp.Nanos);
    }

    private static long ToNanos(CelDuration duration) {
        return checked(duration.Seconds * 1_000_000_000L + duration.Nanos);
    }

    private static CelDuration DurationFromNanos(long nanos) {
        return new(nanos / 1_000_000_000L, (int)(nanos % 1_000_000_000L));
    }

    private static CelTimestamp TimestampFromNanos(long nanos) {
        return new(nanos / 1_000_000_000L, (int)(nanos % 1_000_000_000L));
    }

    private static object? AddDuration(CelTimestamp timestamp, CelDuration duration) {
        var seconds = checked(timestamp.Seconds + duration.Seconds);
        var nanos = timestamp.Nanos + duration.Nanos;
        if (nanos >= 1_000_000_000) {
            seconds = checked(seconds + 1);
            nanos -= 1_000_000_000;
        } else if (nanos < 0) {
            seconds = checked(seconds - 1);
            nanos += 1_000_000_000;
        }

        return seconds is < -62_135_596_800L or > 253_402_300_799L
            ? Error("range")
            : new CelTimestamp(seconds, nanos);
    }

    private static int ParseFractionNanos(string text) {
        var dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) {
            return 0;
        }

        var end = dot + 1;
        while (end < text.Length && char.IsDigit(text[end])) {
            end++;
        }

        var fraction = text.Substring(dot + 1, Math.Min(9, end - dot - 1)).PadRight(9, '0');
        return int.Parse(fraction, CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(CelTimestamp timestamp) {
        var dto = DateTimeOffset.FromUnixTimeSeconds(timestamp.Seconds).ToUniversalTime();
        return timestamp.Nanos == 0
            ? dto.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : dto.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            + "." + timestamp.Nanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0') + "Z";
    }

    private static string FormatDuration(CelDuration duration) {
        var nanos = duration.Nanos == 0 ? string.Empty : "." + duration.Nanos.ToString("D9", CultureInfo.InvariantCulture).TrimEnd('0');
        return duration.Seconds.ToString(CultureInfo.InvariantCulture) + nanos + "s";
    }

    private static object? ParseDuration(string text) {
        var match = Regex.Match(text, "^(?<sign>-)?(?:(?<h>[0-9]+(?:\\.[0-9]+)?)h)?(?:(?<m>[0-9]+(?:\\.[0-9]+)?)m)?(?:(?<s>[0-9]+(?:\\.[0-9]+)?)s)?(?:(?<ms>[0-9]+(?:\\.[0-9]+)?)ms)?(?:(?<us>[0-9]+(?:\\.[0-9]+)?)us)?(?:(?<ns>[0-9]+)ns)?$");
        if (!match.Success || string.IsNullOrEmpty(match.Groups[0].Value.Replace("-", string.Empty, StringComparison.Ordinal))) {
            return Error("invalid duration");
        }

        decimal total = 0;
        if (match.Groups["h"].Success) {
            total += decimal.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture) * 3600;
        }

        if (match.Groups["m"].Success) {
            total += decimal.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) * 60;
        }

        if (match.Groups["s"].Success) {
            total += decimal.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        }

        if (match.Groups["ms"].Success) {
            total += decimal.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture) / 1_000;
        }

        if (match.Groups["us"].Success) {
            total += decimal.Parse(match.Groups["us"].Value, CultureInfo.InvariantCulture) / 1_000_000;
        }

        if (match.Groups["ns"].Success) {
            total += decimal.Parse(match.Groups["ns"].Value, CultureInfo.InvariantCulture) / 1_000_000_000;
        }

        if (match.Groups["sign"].Success) {
            total = -total;
        }

        var seconds = decimal.Truncate(total);
        var nanos = (int)((total - seconds) * 1_000_000_000m);
        if (seconds is < -315_576_000_000m or > 315_576_000_000m) {
            return Error("range");
        }

        return new CelDuration((long)seconds, nanos);
    }

    private sealed class CelKeyComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) { return EqualValue(x, y); }

        public int GetHashCode(object obj) {
            return obj switch {
                byte[] bytes => bytes.Aggregate(17, (hash, b) => hash * 31 + b),
                long i => i.GetHashCode(),
                ulong u when u <= long.MaxValue => ((long)u).GetHashCode(),
                ulong u => u.GetHashCode(),
                double d => d.GetHashCode(),
                _ => obj.GetHashCode(),
            };
        }
    }
}
