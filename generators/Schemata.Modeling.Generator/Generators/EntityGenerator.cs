using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class EntityGenerator
{
    public static void Generate(SourceProductionContext spc, Entity entity, Document doc) {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine($"namespace {doc.Namespace} {{");
        }

        sb.Append($"    public record {entity.Name}");

        GenerateUses(sb, entity.Uses, entity.Bases, doc);

        sb.AppendLine("{");

        GenerateEnums(sb, entity);

        GenerateFields(sb, entity.Fields);

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine("}");
        }

        spc.AddSource($"{entity.Name}", sb.ToString());
    }

    internal static void GenerateUses(
        StringBuilder          sb,
        EquatableArray<Use>    uses,
        EquatableArray<string> bases,
        Document               doc
    ) {
        // Collect all base type names from Uses and Bases
        var names = new List<string>();

        foreach (var use in uses) {
            foreach (var name in use.QualifiedNames) {
                var resolved = name;
                if (doc.Traits.Any(t => t.Name == name)) {
                    resolved = $"I{name}";
                }

                names.Add(resolved);
            }
        }

        foreach (var name in bases) {
            var resolved = name;
            if (doc.Traits.Any(t => t.Name == name)) {
                resolved = $"I{name}";
            }

            if (!names.Contains(resolved)) {
                names.Add(resolved);
            }
        }

        if (names.Count == 0) {
            return;
        }

        sb.Append(" : ");

        for (var i = 0; i < names.Count; i++) {
            if (i > 0) sb.Append(", ");
            sb.Append(names[i]);
        }
    }

    private static void GenerateEnums(StringBuilder sb, Entity entity) {
        foreach (var @enum in entity.Enumerations) {
            EnumGenerator.Generate(sb, @enum, null);
        }
    }

    private static void GenerateFields(StringBuilder sb, EquatableArray<Field> fields) {
        foreach (var field in fields) {
            var type  = field.Type;
            var name  = field.Name;
            var value = "default";

            var clr = Utilities.GetClrType(type);
            if (clr is not null) {
                type = clr.FullName;

                if (type == "System.String") {
                    value = "string.Empty";
                }
            }

            if (field.Nullable) {
                type  += "?";
                value =  "null";
            }

            name = Utilities.ToCamelCase(name);

            sb.AppendLine($"        public {type} {name} {{ get; set; }} = {value};");
        }
    }
}
