using System.Text;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class EnumGenerator
{
    public static void Generate(StringBuilder sb, Enumeration @enum, Document? doc) {
        if (doc is not null && !string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine($"namespace {doc.Namespace} {{");
        }

        sb.AppendLine($"    public enum {@enum.Name} {{");

        GenerateValues(sb, @enum);

        sb.AppendLine("    }");

        if (doc is not null && !string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine("}");
        }
    }

    private static void GenerateValues(StringBuilder sb, Enumeration @enum) {
        foreach (var value in @enum.Values) {
            if (value.Assignment is null) {
                sb.AppendLine($"        {value.Name},");
            } else {
                var body = value.Assignment switch {
                    NumberLiteral n => n.Raw,
                    Literal s       => $"\"{s.Value}\"",
                    Reference r     => r.QualifiedName,
                    var _           => value.Assignment.ToString(),
                };
                sb.AppendLine($"        {value.Name} = {body},");
            }
        }
    }
}
