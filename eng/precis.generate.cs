using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

const string iana = "https://www.iana.org/assignments/precis-tables-6.3.0/precis-tables-6.3.0.xml";
const string unicodeData = "https://www.unicode.org/Public/6.3.0/ucd/UnicodeData.txt";
const string scripts = "https://www.unicode.org/Public/6.3.0/ucd/Scripts.txt";
const string joining = "https://www.unicode.org/Public/6.3.0/ucd/extracted/DerivedJoiningType.txt";

using var client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd("Schemata-Precis-Generator/1.0 (+https://github.com/Cyprin/Schemata)");
var sources = new Dictionary<string, string>();
foreach (var uri in new[] { iana, unicodeData, scripts, joining }) {
    Console.WriteLine($"Downloading {uri}");
    sources[uri] = await client.GetStringAsync(uri);
    Console.WriteLine($"{uri} SHA256 {Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sources[uri])))}");
}

var properties = Merge(ParsePrecis(sources[iana]));
var widths = new SortedDictionary<int, string>();
var lowercase = new SortedDictionary<int, string>();
var bidi = new List<Range<PrecisBidi>>();
var virama = new List<Range<bool>>();
foreach (var line in Lines(sources[unicodeData])) {
    var fields = line.Split(';');
    if (fields.Length < 14) {
        continue;
    }

    var point = ParseHex(fields[0]);
    if (fields[5].StartsWith("<wide>", StringComparison.Ordinal)
        || fields[5].StartsWith("<narrow>", StringComparison.Ordinal)) {
        widths[point] = Codepoints(fields[5][(fields[5].IndexOf(' ') + 1)..]);
    }

    if (fields[13].Length > 0) {
        lowercase[point] = Codepoints(fields[13]);
    }

    var direction = fields[4] switch {
        "L" => PrecisBidi.Left,
        "R" => PrecisBidi.Right,
        "AL" => PrecisBidi.ArabicLetter,
        "EN" => PrecisBidi.EuropeanNumber,
        "ES" => PrecisBidi.EuropeanSeparator,
        "ET" => PrecisBidi.EuropeanTerminator,
        "AN" => PrecisBidi.ArabicNumber,
        "CS" => PrecisBidi.CommonSeparator,
        "BN" => PrecisBidi.BoundaryNeutral,
        "NSM" => PrecisBidi.NonSpacingMark,
        "ON" => PrecisBidi.OtherNeutral,
        _ => PrecisBidi.Other,
    };
    bidi.Add(new(point, point, direction));
    if (fields[3] == "9") {
        virama.Add(new(point, point, true));
    }
}

var script = ParseHashRanges(sources[scripts], value => value switch {
    "Greek" => PrecisScript.Greek,
    "Hebrew" => PrecisScript.Hebrew,
    "Hiragana" => PrecisScript.Hiragana,
    "Katakana" => PrecisScript.Katakana,
    "Han" => PrecisScript.Han,
    _ => PrecisScript.Other,
}).Where(range => range.Value != PrecisScript.Other).ToList();
var joins = ParseHashRanges(sources[joining], value => value switch {
    "L" => PrecisJoining.Left,
    "R" => PrecisJoining.Right,
    "D" => PrecisJoining.Dual,
    "T" => PrecisJoining.Transparent,
    _ => PrecisJoining.Other,
}).Where(range => range.Value != PrecisJoining.Other).ToList();

var output = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath("src/Schemata.Identity.Skeleton/Services/PrecisTables.cs");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var text = Generate(properties, widths, lowercase, Merge(bidi), Merge(virama), Merge(script), Merge(joins));
await File.WriteAllTextAsync(output, text, new UTF8Encoding(false));
Console.WriteLine($"Wrote {output}");

static List<Range<PrecisProperty>> ParsePrecis(string source) {
    XNamespace ns = "http://www.iana.org/assignments";
    var document = XDocument.Parse(source);
    var result = new List<Range<PrecisProperty>>();
    foreach (var record in document.Descendants(ns + "record")) {
        var property = record.Element(ns + "property")?.Value switch {
            "PVALID" => PrecisProperty.Valid,
            "CONTEXTO" => PrecisProperty.ContextO,
            "CONTEXTJ" => PrecisProperty.ContextJ,
            _ => PrecisProperty.Disallowed,
        };
        if (property == PrecisProperty.Disallowed) {
            continue;
        }

        var (start, end) = Range(record.Element(ns + "codepoint")!.Value);
        result.Add(new(start, end, property));
    }

    return result;
}

static IEnumerable<Range<T>> ParseHashRanges<T>(string source, Func<string, T> convert) {
    foreach (var line in Lines(source)) {
        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) {
            continue;
        }

        var (start, end) = Range(parts[0]);
        yield return new(start, end, convert(parts[1]));
    }
}

static IEnumerable<string> Lines(string source) {
    return source.Split('\n').Select(line => line.Split('#')[0].Trim()).Where(line => line.Length > 0);
}

static (int Start, int End) Range(string value) {
    var separator = value.IndexOf('-');
    if (separator >= 0) {
        return (ParseHex(value[..separator]), ParseHex(value[(separator + 1)..]));
    }

    separator = value.IndexOf("..", StringComparison.Ordinal);
    if (separator >= 0) {
        return (ParseHex(value[..separator]), ParseHex(value[(separator + 2)..]));
    }

    return (ParseHex(value), ParseHex(value));
}

static int ParseHex(string value) => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

static string Codepoints(string value) {
    return string.Concat(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(point => char.ConvertFromUtf32(ParseHex(point))));
}

static List<Range<T>> Merge<T>(IEnumerable<Range<T>> source) {
    var result = new List<Range<T>>();
    foreach (var range in source.OrderBy(range => range.Start).ThenBy(range => range.End)) {
        if (result.Count > 0 && EqualityComparer<T>.Default.Equals(result[^1].Value, range.Value)
            && result[^1].End >= range.Start - 1) {
            result[^1] = result[^1] with { End = Math.Max(result[^1].End, range.End) };
        } else {
            result.Add(range);
        }
    }

    return result;
}

static string Generate(
    IReadOnlyList<Range<PrecisProperty>> properties,
    IReadOnlyDictionary<int, string> widths,
    IReadOnlyDictionary<int, string> lowercase,
    IReadOnlyList<Range<PrecisBidi>> bidi,
    IReadOnlyList<Range<bool>> virama,
    IReadOnlyList<Range<PrecisScript>> scripts,
    IReadOnlyList<Range<PrecisJoining>> joins) {
    var output = new StringBuilder();
    output.AppendLine("// <auto-generated />");
    output.AppendLine("#nullable enable");
    output.AppendLine("// IANA PRECIS Derived Property Value 6.3.0 and Unicode 6.3.0 UCD.");
    output.AppendLine("using System;");
    output.AppendLine();
    output.AppendLine("namespace Schemata.Identity.Skeleton.Services;");
    output.AppendLine();
    output.AppendLine("internal enum PrecisProperty : byte { Disallowed, Valid, ContextO, ContextJ }");
    output.AppendLine("internal enum PrecisBidi : byte { Other, Left, Right, ArabicLetter, EuropeanNumber, EuropeanSeparator, EuropeanTerminator, ArabicNumber, CommonSeparator, BoundaryNeutral, NonSpacingMark, OtherNeutral }");
    output.AppendLine("internal enum PrecisJoining : byte { Other, Left, Right, Dual, Transparent }");
    output.AppendLine("internal enum PrecisScript : byte { Other, Greek, Hebrew, Hiragana, Katakana, Han }");
    output.AppendLine("internal readonly record struct PrecisRange<T>(int Start, int End, T Value);");
    output.AppendLine();
    output.AppendLine("internal static class PrecisTables");
    output.AppendLine("{");
    EmitRanges(output, "PropertyData", "PrecisProperty", properties);
    EmitRanges(output, "BidiData", "PrecisBidi", bidi);
    EmitRanges(output, "JoiningData", "PrecisJoining", joins);
    EmitRanges(output, "ScriptData", "PrecisScript", scripts);
    EmitRanges(output, "ViramaData", "bool", virama);
    EmitMap(output, "WidthData", widths);
    EmitMap(output, "LowerData", lowercase);
    output.AppendLine("    public static PrecisProperty Property(int point) => Find(PropertyData, point, PrecisProperty.Disallowed);");
    output.AppendLine("    public static PrecisBidi Bidi(int point) => Find(BidiData, point, PrecisBidi.Other);");
    output.AppendLine("    public static PrecisJoining Joining(int point) => Find(JoiningData, point, PrecisJoining.Other);");
    output.AppendLine("    public static PrecisScript Script(int point) => Find(ScriptData, point, PrecisScript.Other);");
    output.AppendLine("    public static bool IsVirama(int point) => Find(ViramaData, point, false);");
    output.AppendLine("    public static string? Width(int point) => Map(WidthData, WidthValues, point);");
    output.AppendLine("    public static string? Lower(int point) => Map(LowerData, LowerValues, point);");
    output.AppendLine();
    output.AppendLine("    private static T Find<T>(ReadOnlySpan<PrecisRange<T>> ranges, int point, T fallback) {");
    output.AppendLine("        var low = 0;");
    output.AppendLine("        var high = ranges.Length - 1;");
    output.AppendLine("        while (low <= high) {");
    output.AppendLine("            var middle = low + (high - low) / 2;");
    output.AppendLine("            var range = ranges[middle];");
    output.AppendLine("            if (point < range.Start) high = middle - 1;");
    output.AppendLine("            else if (point > range.End) low = middle + 1;");
    output.AppendLine("            else return range.Value;");
    output.AppendLine("        }");
    output.AppendLine("        return fallback;");
    output.AppendLine("    }");
    output.AppendLine();
    output.AppendLine("    private static string? Map(ReadOnlySpan<int> points, ReadOnlySpan<string> values, int point) {");
    output.AppendLine("        var index = points.BinarySearch(point);");
    output.AppendLine("        return index < 0 ? null : values[index];");
    output.AppendLine("    }");
    output.AppendLine("}");
    return output.ToString();
}

static void EmitRanges<T>(StringBuilder output, string name, string type, IReadOnlyList<Range<T>> ranges) {
    output.AppendLine($"    private static readonly PrecisRange<{type}>[] {name} = [");
    foreach (var range in ranges) {
        output.AppendLine($"        new(0x{range.Start:X}, 0x{range.End:X}, {Value(range.Value)}),");
    }
    output.AppendLine("    ];");
    output.AppendLine();
}

static void EmitMap(StringBuilder output, string name, IReadOnlyDictionary<int, string> values) {
    output.AppendLine($"    private static readonly int[] {name} = [");
    output.AppendLine(string.Join(", ", values.Keys.Select(point => $"0x{point:X}")));
    output.AppendLine("    ];");
    output.AppendLine($"    private static readonly string[] {name[..^4]}Values = [");
    foreach (var value in values.Values) {
        output.AppendLine($"        {Literal(value)},");
    }
    output.AppendLine("    ];");
    output.AppendLine();
}

static string Value<T>(T value) {
    return value switch {
        bool boolean => boolean ? "true" : "false",
        PrecisProperty property => $"PrecisProperty.{property}",
        PrecisBidi bidi => $"PrecisBidi.{bidi}",
        PrecisJoining joining => $"PrecisJoining.{joining}",
        PrecisScript script => $"PrecisScript.{script}",
        _ => throw new InvalidOperationException("Unsupported generated table value."),
    };
}

static string Literal(string value) {
    return "\"" + string.Concat(value.EnumerateRunes().Select(rune => rune.Value > 0xFFFF
        ? $"\\U{rune.Value:X8}"
        : $"\\u{rune.Value:X4}")) + "\"";
}

internal enum PrecisProperty { Disallowed, Valid, ContextO, ContextJ }
internal enum PrecisBidi { Other, Left, Right, ArabicLetter, EuropeanNumber, EuropeanSeparator, EuropeanTerminator, ArabicNumber, CommonSeparator, BoundaryNeutral, NonSpacingMark, OtherNeutral }
internal enum PrecisJoining { Other, Left, Right, Dual, Transparent }
internal enum PrecisScript { Other, Greek, Hebrew, Hiragana, Katakana, Han }
internal readonly record struct Range<T>(int Start, int End, T Value);
