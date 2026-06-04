using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Schemata.Expressions.Cel.Tests.Conformance;

public static class CelSpecLoader
{
    private static readonly Regex Name = new("name: \\\"(?<value>[^\\\"]+)\\\"", RegexOptions.Compiled);

    private static readonly Regex Expr = new("expr: (?<quote>[\\\"'])(?<value>.*?)(?<!\\\\)\\k<quote>",
                                             RegexOptions.Compiled);

    private static readonly Regex Bool   = new("bool_value: (?<value>true|false)", RegexOptions.Compiled);
    private static readonly Regex Int    = new("int64_value: (?<value>-?[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex UInt   = new("uint64_value: (?<value>[0-9]+)", RegexOptions.Compiled);
    private static readonly Regex Double = new("double_value: (?<value>-?[0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled);

    private static readonly Regex String = new("string_value: \\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"",
                                               RegexOptions.Compiled);

    private static readonly Regex Bytes = new("bytes_value: \\\"(?<value>(?:\\\\.|[^\\\\\\\"])*?)\\\"",
                                              RegexOptions.Compiled);

    private static readonly Regex Error = new("eval_error:.*?message: \\\"(?<value>[^\\\"]+)\\\"",
                                              RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Null = new("null_value:", RegexOptions.Compiled);

    public static IEnumerable<object[]> BasicSelfEvalCases() {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                                                 "../../../../../specs/cel/tests/simple/testdata/basic.textproto"));
        var skips = LoadSkips();

        foreach (var test in ParseTests(File.ReadAllText(path))) {
            if (skips.Contains(test.Name)) {
                continue;
            }

            if (test.Value is not null) {
                yield return [test.Name, test.Expression, test.Value];
            }
        }
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

    private static IEnumerable<Case> ParseTests(string content) {
        foreach (var block in content.Split("\n  test {")) {
            var name = Name.Match(block);
            var expr = Expr.Match(block);
            if (!name.Success || !expr.Success) {
                continue;
            }

            if (!name.Groups["value"].Value.StartsWith("self_eval_", StringComparison.Ordinal)) {
                continue;
            }

            yield return new(name.Groups["value"].Value, DecodeTextProtoString(expr.Groups["value"].Value),
                             ParseValue(block));
        }
    }

    private static CelSpecValue? ParseValue(string block) {
        if (block.Contains("list_value", StringComparison.Ordinal)) {
            var values       = new List<object?>();
            var listIntMatch = Int.Match(block);
            if (listIntMatch.Success) {
                values.Add(long.Parse(listIntMatch.Groups["value"].Value, CultureInfo.InvariantCulture));
            }

            return new(values);
        }

        if (block.Contains("map_value", StringComparison.Ordinal)) {
            var strings = new List<string>();
            foreach (Match match in String.Matches(block)) {
                strings.Add(DecodeTextProtoString(match.Groups["value"].Value));
            }

            var map = new Dictionary<object, object?>();
            if (strings.Count >= 2) {
                map[strings[0]] = strings[1];
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
            return CelSpecValue.Double(double.Parse(doubleMatch.Groups["value"].Value, CultureInfo.InvariantCulture));
        }

        var stringMatch = String.Match(block);
        if (stringMatch.Success) {
            return CelSpecValue.String(DecodeTextProtoString(stringMatch.Groups["value"].Value));
        }

        var bytesMatch = Bytes.Match(block);
        if (bytesMatch.Success) {
            return CelSpecValue.Bytes(DecodeTextProtoBytes(bytesMatch.Groups["value"].Value));
        }

        var errorMatch = Error.Match(block);
        if (errorMatch.Success) {
            return CelSpecValue.Error(DecodeTextProtoString(errorMatch.Groups["value"].Value));
        }

        return Null.IsMatch(block) ? CelSpecValue.Null() : null;
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
                case 'a':
                    bytes.Add((byte)'\a');
                    break;
                case 'b':
                    bytes.Add((byte)'\b');
                    break;
                case 'f':
                    bytes.Add((byte)'\f');
                    break;
                case 'n':
                    bytes.Add((byte)'\n');
                    break;
                case 'r':
                    bytes.Add((byte)'\r');
                    break;
                case 't':
                    bytes.Add((byte)'\t');
                    break;
                case 'v':
                    bytes.Add((byte)'\v');
                    break;
                case '\\':
                case '\"':
                case '\'':
                    bytes.Add((byte)escaped);
                    break;
                case 'x':
                    bytes.Add(byte.Parse(source.Substring(i + 1, 2), NumberStyles.HexNumber,
                                         CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                default:
                    if (escaped >= '0' && escaped <= '7') {
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

    #region Nested type: Case

    private sealed record Case(string Name, string Expression, CelSpecValue? Value);

    #endregion
}
