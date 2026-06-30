using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Schemata.Expressions.Cel.Tests.Conformance;

public static class CelSpecLoader
{
    private static readonly string[] Suites = [
        "basic", "comparisons", "integer_math", "fp_math", "logic", "lists", "string", "conversions", "macros",
        "macros2", "timestamps",
    ];

    private static readonly Regex Name = new("name: \\\"(?<value>[^\\\"]+)\\\"", RegexOptions.Compiled);
    private static readonly Regex Expr = new("expr: (?<quote>[\\\"'])(?<value>.*?)(?<!\\\\)\\k<quote>", RegexOptions.Compiled);
    private static readonly Regex Key = new("key: \\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"", RegexOptions.Compiled);
    private static readonly Regex Bool = new("bool_value: (?<value>true|false)", RegexOptions.Compiled);
    private static readonly Regex Int = new("int64_value: (?<value>-?[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex UInt = new("uint64_value: (?<value>[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex Double = new("double_value: (?<value>-?(?:[0-9]+(?:\\.[0-9]+)?(?:e[+-]?[0-9]+)?|inf|Infinity|NaN))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex String = new("string_value: \\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"", RegexOptions.Compiled);
    private static readonly Regex Bytes = new("bytes_value: \\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"", RegexOptions.Compiled);
    private static readonly Regex Type = new("type_value: \\\"(?<value>[^\\\"]+)\\\"", RegexOptions.Compiled);
    private static readonly Regex Seconds = new("seconds: (?<value>-?[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex Nanos = new("nanos: (?<value>-?[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex Error = new("eval_error\\s*:?\\s*\\{.*?message\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Null = new("null_value:", RegexOptions.Compiled);

    public static IEnumerable<object[]> Cases() {
        var skips = LoadSkips();
        foreach (var suite in Suites) {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                     $"../../../../../specs/cel/tests/simple/testdata/{suite}.textproto"));
            foreach (var test in ParseTests(suite, File.ReadAllText(path))) {
                if (skips.Contains($"{test.Suite}/{test.Name}") || skips.Contains(test.Name)) {
                    continue;
                }

                yield return [test];
            }
        }
    }

    public static IEnumerable<object[]> BasicSelfEvalCases() {
        return Cases().Where(row => ((CelSpecCase)row[0]).Suite == "basic");
    }

    private static IEnumerable<CelSpecCase> ParseTests(string suite, string content) {
        foreach (var block in Blocks(content, "test")) {
            var name = Name.Match(block);
            var expr = Expr.Match(block);
            if (!name.Success || !expr.Success || block.Contains("parse_error", StringComparison.Ordinal)) {
                continue;
            }

            var expected = ParseExpected(block);
            if (expected is null) {
                continue;
            }

            yield return new(suite, name.Groups["value"].Value, DecodeTextProtoString(expr.Groups["value"].Value),
                             ParseBindings(block), expected);
        }
    }

    private static CelSpecValue? ParseExpected(string block) {
        var error = Error.Match(block);
        if (error.Success) {
            return CelSpecValue.Error(DecodeTextProtoString(error.Groups["value"].Value));
        }

        var value = FieldBlockTopLevel(block, "value");
        return value is null ? null : ParseValue(value);
    }

    private static Dictionary<string, object?> ParseBindings(string block) {
        var bindings = new Dictionary<string, object?>();
        foreach (var binding in BlocksTopLevel(block, "bindings")) {
            var key = Key.Match(binding);
            var value = FieldBlockTopLevel(binding, "value");
            if (!key.Success || value is null) {
                continue;
            }

            bindings[DecodeTextProtoString(key.Groups["value"].Value)] = ParseValue(value)?.Value;
        }

        return bindings;
    }

    private static CelSpecValue? ParseValue(string block) {
        if (block.Contains("type.googleapis.com/google.protobuf.Duration", StringComparison.Ordinal)) {
            var seconds = Seconds.Match(block);
            var nanos = Nanos.Match(block);
            return new(new CelDuration(seconds.Success ? long.Parse(seconds.Groups["value"].Value, CultureInfo.InvariantCulture) : 0,
                                       nanos.Success ? int.Parse(nanos.Groups["value"].Value, CultureInfo.InvariantCulture) : 0));
        }

        if (block.Contains("type.googleapis.com/google.protobuf.Timestamp", StringComparison.Ordinal)) {
            var seconds = Seconds.Match(block);
            var nanos = Nanos.Match(block);
            return new(new CelTimestamp(seconds.Success ? long.Parse(seconds.Groups["value"].Value, CultureInfo.InvariantCulture) : 0,
                                        nanos.Success ? int.Parse(nanos.Groups["value"].Value, CultureInfo.InvariantCulture) : 0));
        }

        if (block.Contains("list_value", StringComparison.Ordinal)) {
            var list = new List<object?>();
            var listBlock = FieldBlock(block, "list_value") ?? block;
            foreach (var item in Blocks(listBlock, "values")) {
                list.Add(ParseValue(item)?.Value);
            }

            return new(list);
        }

        if (block.Contains("map_value", StringComparison.Ordinal)) {
            var map = new Dictionary<object, object?>(CelValues.KeyComparer);
            var mapBlock = FieldBlock(block, "map_value") ?? block;
            foreach (var entry in Blocks(mapBlock, "entries")) {
                var key = FieldBlockTopLevel(entry, "key");
                var value = FieldBlockTopLevel(entry, "value");
                if (key is null || value is null) {
                    continue;
                }

                map[ParseValue(key)!.Value!] = ParseValue(value)?.Value;
            }

            return new(map);
        }

        var boolMatch = Bool.Match(block);
        if (boolMatch.Success) {
            return CelSpecValue.Bool(bool.Parse(boolMatch.Groups["value"].Value));
        }

        var uintMatch = UInt.Match(block);
        if (uintMatch.Success) {
            return CelSpecValue.UInt(ulong.Parse(uintMatch.Groups["value"].Value, CultureInfo.InvariantCulture));
        }

        var intMatch = Int.Match(block);
        if (intMatch.Success) {
            return CelSpecValue.Int(long.Parse(intMatch.Groups["value"].Value, CultureInfo.InvariantCulture));
        }

        var doubleMatch = Double.Match(block);
        if (doubleMatch.Success) {
            return CelSpecValue.Double(ParseDouble(doubleMatch.Groups["value"].Value));
        }

        var stringMatch = String.Match(block);
        if (stringMatch.Success) {
            return CelSpecValue.String(DecodeTextProtoString(stringMatch.Groups["value"].Value));
        }

        var bytesMatch = Bytes.Match(block);
        if (bytesMatch.Success) {
            return CelSpecValue.Bytes(DecodeTextProtoBytes(bytesMatch.Groups["value"].Value));
        }

        var typeMatch = Type.Match(block);
        if (typeMatch.Success) {
            return CelSpecValue.Type(DecodeTextProtoString(typeMatch.Groups["value"].Value));
        }

        return Null.IsMatch(block) ? CelSpecValue.Null() : null;
    }

    private static double ParseDouble(string value) {
        return value.ToLowerInvariant() switch {
            "inf" or "infinity" => double.PositiveInfinity,
            "-inf" or "-infinity" => double.NegativeInfinity,
            "nan" => double.NaN,
            var text => double.Parse(text, CultureInfo.InvariantCulture),
        };
    }

    private static IEnumerable<string> Blocks(string content, string field) {
        var pattern = new Regex($"(?<![A-Za-z0-9_]){Regex.Escape(field)}(?![A-Za-z0-9_])\\s*:?\\s*{{");
        for (var search = 0; search < content.Length;) {
            var match = pattern.Match(content, search);
            if (!match.Success) {
                yield break;
            }

            var brace = match.Index + match.Value.LastIndexOf('{');
            var end = MatchingBrace(content, brace);
            yield return content.Substring(brace + 1, end - brace - 1);
            search = end + 1;
        }
    }

    private static string? FieldBlock(string content, string field) {
        return Blocks(content, field).FirstOrDefault();
    }

    private static IEnumerable<string> BlocksTopLevel(string content, string field) {
        var pattern = new Regex($"(?<![A-Za-z0-9_]){Regex.Escape(field)}(?![A-Za-z0-9_])\\s*:?\\s*{{");
        for (var search = 0; search < content.Length;) {
            var match = pattern.Match(content, search);
            if (!match.Success) {
                yield break;
            }

            if (DepthAt(content, match.Index) != 0) {
                search = match.Index + match.Length;
                continue;
            }

            var brace = match.Index + match.Value.LastIndexOf('{');
            var end = MatchingBrace(content, brace);
            yield return content.Substring(brace + 1, end - brace - 1);
            search = end + 1;
        }
    }

    private static string? FieldBlockTopLevel(string content, string field) {
        return BlocksTopLevel(content, field).FirstOrDefault();
    }

    private static int DepthAt(string content, int offset) {
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i < offset; i++) {
            var c = content[i];
            if (quote != '\0') {
                if (c == '\\') {
                    i++;
                    continue;
                }

                if (c == quote) {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"') {
                quote = c;
            } else if (c == '{') {
                depth++;
            } else if (c == '}') {
                depth--;
            }
        }

        return depth;
    }

    private static int MatchingBrace(string content, int open) {
        var depth = 0;
        var quote = '\0';
        for (var i = open; i < content.Length; i++) {
            var c = content[i];
            if (quote != '\0') {
                if (c == '\\') {
                    i++;
                    continue;
                }

                if (c == quote) {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"') {
                quote = c;
                continue;
            }

            if (c == '{') {
                depth++;
            } else if (c == '}' && --depth == 0) {
                return i;
            }
        }

        throw new InvalidDataException("Unbalanced textproto block.");
    }

    private static HashSet<string> LoadSkips() {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                 "../../../../../tests/Schemata.Expressions.Cel.Tests/Conformance/cel-spec-skips.txt"));
        var skips = new HashSet<string>();
        foreach (var line in File.ReadAllLines(path)) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }

            skips.Add(trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        }

        return skips;
    }

    private static string DecodeTextProtoString(string source) {
        return Encoding.UTF8.GetString(DecodeTextProtoBytes(source));
    }

    private static byte[] DecodeTextProtoBytes(string source) {
        var bytes = new List<byte>(source.Length);
        for (var i = 0; i < source.Length; i++) {
            var c = source[i];
            if (c != '\\' || i + 1 >= source.Length) {
                bytes.AddRange(Encoding.UTF8.GetBytes([c]));
                continue;
            }

            var escaped = source[++i];
            switch (escaped) {
                case 'a': bytes.Add((byte)'\a'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'v': bytes.Add((byte)'\v'); break;
                case '\\':
                case '"':
                case '\'':
                    bytes.Add((byte)escaped);
                    break;
                case 'x':
                    bytes.Add(byte.Parse(source.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                case 'u':
                    bytes.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(int.Parse(source.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                    i += 4;
                    break;
                case 'U':
                    bytes.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(int.Parse(source.AsSpan(i + 1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
                    i += 8;
                    break;
                default:
                    if (escaped is >= '0' and <= '7') {
                        var octal = source.Substring(i, Math.Min(3, source.Length - i));
                        bytes.Add(Convert.ToByte(octal, 8));
                        i += octal.Length - 1;
                    } else {
                        bytes.Add((byte)escaped);
                    }

                    break;
            }
        }

        return bytes.ToArray();
    }
}
