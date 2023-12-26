using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.DSL.Terms;

// ReSharper disable once CheckNamespace
namespace Schemata.DSL;

public static class TraitGenerator
{
    public static void Generate(this Trait trait, SourceProductionContext spc, Mark mark) {
        var name = $"I{trait.Name}";

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(mark.Namespace?.Name)) {
            sb.AppendLine($"namespace {mark.Namespace?.Name} {{");
        }

        sb.Append($"    public interface {name}");

        EntityGenerator.GenerateUses(sb, trait, mark);

        sb.AppendLine("{");

        GenerateFields(sb, trait, mark);

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(mark.Namespace?.Name)) {
            sb.AppendLine("}");
        }

        spc.AddSource($"{name}", sb.ToString());
    }

    private static void GenerateFields(StringBuilder sb, Entity entity, Mark mark) {
        if (entity.Fields is null) {
            return;
        }

        foreach (var field in entity.Fields) {
            if (field.Value.Inherited) {
                continue;
            }

            var type = field.Value.Type;
            var name = field.Value.Name;

            var clr = Utilities.GetClrType(type);
            if (clr is not null) {
                type = clr.FullName;
            }

            if (field.Value.Nullable) {
                type += "?";
            }

            name = Utilities.ToCamelCase(name);

            sb.AppendLine($"        public {type} {name} {{ get; set; }}");
        }
    }
}
