using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class TraitGenerator
{
    public static void Generate(SourceProductionContext spc, Trait trait, Document doc) {
        var name = $"I{trait.Name}";

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine($"namespace {doc.Namespace} {{");
        }

        sb.Append($"    public interface {name}");

        EntityGenerator.GenerateUses(sb, trait.Uses, trait.Bases, doc);

        sb.AppendLine("{");

        GenerateFields(sb, trait.Fields);

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine("}");
        }

        spc.AddSource($"{name}", sb.ToString());
    }

    private static void GenerateFields(StringBuilder sb, EquatableArray<Field> fields) {
        foreach (var field in fields) {
            var type = field.Type;
            var name = field.Name;

            var clr = Utilities.GetClrType(type);
            if (clr is not null) {
                type = clr.FullName;
            }

            if (field.Nullable) {
                type += "?";
            }

            name = Utilities.ToCamelCase(name);

            sb.AppendLine($"        public {type} {name} {{ get; set; }}");
        }
    }
}
