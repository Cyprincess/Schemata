using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.DSL.Terms;

// ReSharper disable once CheckNamespace
namespace Schemata.DSL;

public static class EntityGenerator
{
    public static void Generate(this Entity entity, SourceProductionContext spc, Mark mark) {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(mark.Namespace?.Name)) {
            sb.AppendLine($"namespace {mark.Namespace?.Name} {{");
        }

        sb.Append($"    public record {entity.Name}");

        GenerateUses(sb, entity, mark);

        sb.AppendLine("{");

        GenerateEnums(sb, entity, mark);

        GenerateFields(sb, entity, mark);

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(mark.Namespace?.Name)) {
            sb.AppendLine("}");
        }

        spc.AddSource($"{entity.Name}", sb.ToString());
    }

    public static void GenerateUses(StringBuilder sb, Entity entity, Mark mark) {
        if (entity.Uses is null) {
            return;
        }

        sb.Append(" : ");

        foreach (var use in entity.Uses) {
            var name = use.Name;
            if (mark.Traits?.ContainsKey(name) == true) {
                name = $"I{name}";
            }

            sb.Append($"{name}, ");
        }

        sb.Remove(sb.Length - 2, 2);
    }

    private static void GenerateEnums(StringBuilder sb, Entity entity, Mark mark) {
        if (mark.Enums is null) {
            return;
        }

        foreach (var @enum in mark.Enums) {
            var scope = @enum.Key;
            var name  = @enum.Value.Name;
            scope = scope.Remove(scope.Length - name.Length - 1, name.Length + 1);
            if (string.IsNullOrWhiteSpace(scope)) {
                continue;
            }

            if (mark.Tables?.ContainsKey(scope) == true) {
                @enum.Value.Generate(sb, null);
            }
        }
    }

    private static void GenerateFields(StringBuilder sb, Entity entity, Mark mark) {
        if (entity.Fields is null) {
            return;
        }

        foreach (var field in entity.Fields) {
            var type  = field.Value.Type;
            var name  = field.Value.Name;
            var value = "default";

            var clr = Utilities.GetClrType(type);
            if (clr is not null) {
                type = clr.FullName;

                if (type == "System.String") {
                    value = "string.Empty";
                }
            }

            if (field.Value.Nullable) {
                type  += "?";
                value =  "null";
            }

            name = Utilities.ToCamelCase(name);

            sb.AppendLine($"        public {type} {name} {{ get; set; }} = {value};");
        }
    }
}
