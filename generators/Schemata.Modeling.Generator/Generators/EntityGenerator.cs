using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class EntityGenerator
{
    public static void Generate(SourceProductionContext spc, Entity entity, Document doc, Compilation compilation, Location location) {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine($"namespace {doc.Namespace} {{");
        }

        sb.Append($"    public record {entity.Name}");

        GenerateUses(sb, entity.Uses, entity.Bases, doc);

        sb.AppendLine("{");

        GenerateEnums(spc, sb, entity, location);

        if (!InterfaceProperties.Generate(spc, sb, entity, doc, compilation, location)) return;

        sb.AppendLine("    }");

        if (!string.IsNullOrWhiteSpace(doc.Namespace)) {
            sb.AppendLine("}");
        }

        spc.AddSource(SourceIdentity.Hint("record", doc.Namespace, entity.Name), sb.ToString());
    }

    internal static void GenerateUses(
        StringBuilder          sb,
        EquatableArray<Use>    uses,
        EquatableArray<string> bases,
        Document               doc
    ) {
        // Every Use and Bases reference resolves through this single dedup owner:
        // duplicate references are legal input and must emit each base type once.
        // Resolving before deduping also collapses a trait reference and a direct
        // reference to its generated interface into one entry.
        var names = new List<string>();
        var seen  = new HashSet<string>(StringComparer.Ordinal);

        void Add(string name) {
            var resolved = doc.Traits.Any(t => t.Name == name) ? $"I{name}" : name;
            if (seen.Add(resolved)) {
                names.Add(resolved);
            }
        }

        foreach (var use in uses) {
            foreach (var name in use.QualifiedNames) {
                Add(name);
            }
        }

        foreach (var name in bases) {
            Add(name);
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

    private static void GenerateEnums(SourceProductionContext spc, StringBuilder sb, Entity entity, Location location) {
        foreach (var @enum in entity.Enumerations) {
            var supported = true;
            foreach (var value in @enum.Values) {
                if (DocumentGenerator.IsSupportedEnumAssignment(value.Assignment)) {
                    continue;
                }
                supported = false;
                spc.ReportDiagnostic(Diagnostic.Create(DocumentGenerator.UnsupportedEnumAssignment, location, @enum.Name, value.Name, value.Assignment));
            }
            if (!supported) {
                continue;
            }

            EnumGenerator.Generate(sb, @enum, null);
        }
    }

    internal static void GenerateFields(StringBuilder sb, IEnumerable<Field> fields) {
        foreach (var field in fields) {
            var type  = field.Type;
            var name  = field.Name;
            var value = "default";

            var clr = Utilities.GetClrType(type);
            if (clr is not null) {
                type = clr.FullName;

                if (clr == typeof(string)) {
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
