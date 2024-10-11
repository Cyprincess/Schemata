using System.Text;
using Schemata.DSL.Terms;

// ReSharper disable once CheckNamespace
namespace Schemata.DSL;

public static class EnumGenerator
{
    public static void Generate(this Enum @enum, StringBuilder sb, Mark? mark) {
        if (!string.IsNullOrWhiteSpace(mark?.Namespace?.Name)) {
            sb.AppendLine($"namespace {mark?.Namespace?.Name} {{");
        }

        sb.AppendLine($"    public enum {@enum.Name} {{");

        GenerateValues(sb, @enum, mark);

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(mark?.Namespace?.Name)) {
            sb.AppendLine("}");
        }
    }

    private static void GenerateValues(StringBuilder sb, Enum @enum, Mark? mark) {
        if (@enum.Values is null) {
            return;
        }

        foreach (var value in @enum.Values) {
            sb.AppendLine(value.Value.Name == value.Value.Body
                              ? $"        {value.Value.Name},"
                              : $"        {value.Value.Name} = {value.Value.Body},");
        }
    }
}
