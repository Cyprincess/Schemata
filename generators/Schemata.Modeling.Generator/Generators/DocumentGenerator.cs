using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class DocumentGenerator
{
    internal static readonly DiagnosticDescriptor UnsupportedEnumAssignment = new(
        "SKM003",
        "Unsupported enum value assignment",
        "Enum '{0}' value '{1}' has assignment {2}; enum values support decimal integer literals within Int32 range",
        "Modeling",
        DiagnosticSeverity.Error,
        true);

    public static void Generate(SourceProductionContext spc, Document doc, Compilation compilation, Location location) {
        GenerateEnums(spc, doc, location);

        GenerateTraits(spc, doc);

        GenerateEntities(spc, doc, compilation, location);
    }

    private static void GenerateEnums(SourceProductionContext spc, Document doc, Location location) {
        foreach (var @enum in doc.Enumerations) {
            var supported = true;
            foreach (var value in @enum.Values) {
                if (IsSupportedEnumAssignment(value.Assignment)) {
                    continue;
                }
                supported = false;
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedEnumAssignment, location, @enum.Name, value.Name, value.Assignment));
            }
            if (!supported) {
                continue;
            }

            var sb = new StringBuilder();
            EnumGenerator.Generate(sb, @enum, doc);
            spc.AddSource(SourceIdentity.Hint("enum", doc.Namespace, @enum.Name), sb.ToString());
        }
    }

    internal static bool IsSupportedEnumAssignment(IExpression? assignment) {
        if (assignment is null) {
            return true;
        }

        var raw = assignment is NumberLiteral { Raw: var literal } ? literal : null;
        if (raw is null || raw.Length == 0) {
            return false;
        }

        var digits = raw[0] == '-' ? raw[1..] : raw;
        return digits.Length > 0
            && digits.All(digit => digit >= '0' && digit <= '9')
            && decimal.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value)
            && value >= int.MinValue
            && value <= int.MaxValue;
    }

    private static void GenerateTraits(SourceProductionContext spc, Document doc) {
        foreach (var trait in doc.Traits) {
            TraitGenerator.Generate(spc, trait, doc);
        }
    }

    private static void GenerateEntities(SourceProductionContext spc, Document doc, Compilation compilation, Location location) {
        foreach (var entity in doc.Entities) {
            EntityGenerator.Generate(spc, entity, doc, compilation, location);
        }
    }
}
